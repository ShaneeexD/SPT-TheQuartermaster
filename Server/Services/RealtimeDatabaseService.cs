using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Google.Cloud.Firestore;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class RealtimeDatabaseService(
    ISptLogger<RealtimeDatabaseService> logger,
    ConfigService configService,
    FirebaseAuthService firebaseAuthService
)
{
    private readonly HttpClient _httpClient = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private DateTime _lastExpiredCleanup = DateTime.MinValue;
    private string _instanceId = $"{Environment.MachineName}-{Process.GetCurrentProcess().Id}-{Guid.NewGuid():N}";

    public bool IsEnabled { get; private set; }
    public string InstanceId => _instanceId;

    private const string RootPath = "quartermaster";
    private const int CataloguePageSize = 50;
    private const int MaxPurchaseRetries = 3;

    public async Task InitialiseAsync()
    {
        if (!configService.Config.ModEnabled)
        {
            IsEnabled = false;
            logger.Warning("[TheQuartermaster] Realtime Database disabled (mod disabled).");
            return;
        }

        if (!string.Equals(configService.Config.MarketplaceStorage, "realtimeDatabase", StringComparison.OrdinalIgnoreCase))
        {
            IsEnabled = false;
            logger.Info("[TheQuartermaster] Realtime Database not selected as marketplace backend.");
            return;
        }

        if (string.IsNullOrWhiteSpace(ResolveDatabaseUrl()) ||
            string.IsNullOrWhiteSpace(configService.Config.FirebaseProjectId) ||
            string.IsNullOrWhiteSpace(configService.Config.FirebaseApiKey))
        {
            IsEnabled = false;
            logger.Error("[TheQuartermaster] Realtime Database config missing project_id/api_key/database_url.");
            return;
        }

        try
        {
            await firebaseAuthService.InitialiseAsync();
            await firebaseAuthService.GetIdTokenAsync();

            IsEnabled = true;
            logger.Info($"[TheQuartermaster] Realtime Database initialised for {ResolveDatabaseUrl()}.");
        }
        catch (Exception ex)
        {
            IsEnabled = false;
            logger.Error($"[TheQuartermaster] Realtime Database initialisation failed: {ex.Message}", ex);
        }
    }

    public async Task<QuartermasterListing?> UploadListingAsync(QuartermasterListing listing)
    {
        if (!IsEnabled)
        {
            return null;
        }

        try
        {
            listing.Id ??= GenerateListingId();
            listing.SellerUid ??= firebaseAuthService.Uuid;
            listing.Status = ListingStatus.Active;

            var now = DateTime.UtcNow;
            listing.CreatedAt ??= Timestamp.FromDateTime(now);
            listing.ExpiresAt ??= Timestamp.FromDateTime(now.AddSeconds(QuartermasterConstants.Marketplace.ListingDurationSeconds));

            var quantity = GetListingQuantity(listing.ItemTreeJson);
            var data = ToRtdbListing(listing);
            var state = ToRtdbListingState(listing, quantity);
            state.RemainingQuantity = quantity;
            state.Status = ListingStatus.Active;

            await PutJsonAsync($"listings/available/{listing.Id}", data);
            await PutJsonAsync($"listingStates/{listing.Id}", state);
            await PutJsonAsync(GetExpiryIndexPath(ToUnixSeconds(listing.ExpiresAt), listing.Id), true);

            logger.Debug($"[TheQuartermaster] Uploaded listing {listing.Id} to RTDB.");
            return ToQuartermasterListing(data, state, listing.Id);
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to upload listing to RTDB: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<List<QuartermasterListing>> GetActiveListingsAsync()
    {
        var result = new List<QuartermasterListing>();
        if (!IsEnabled)
        {
            return result;
        }

        try
        {
            var listingsTask = GetDictionaryAsync<RtdbListing>("listings/available");
            var statesTask = GetDictionaryAsync<RtdbListingState>("listingStates");
            await Task.WhenAll(listingsTask, statesTask);

            var listings = await listingsTask;
            Dictionary<string, RtdbListingState> states = await statesTask ?? new Dictionary<string, RtdbListingState>();

            logger.Debug($"[TheQuartermaster] RTDB raw listings count: {listings?.Count ?? 0}, raw states count: {states?.Count ?? 0}.");

            if (listings is null)
            {
                logger.Info("[TheQuartermaster] No listings returned from RTDB; trader will have 0 items.");
                return result;
            }

            if (listings.Count == 0)
            {
                logger.Info("[TheQuartermaster] RTDB has no active listings; trader will have 0 items.");
                return result;
            }

            var now = DateTime.UtcNow;
            foreach (var (id, data) in listings)
            {
                states.TryGetValue(id, out var state);
                state ??= new RtdbListingState
                {
                    Status = ListingStatus.Active,
                    RemainingQuantity = GetListingQuantity(data.ItemTreeJson)
                };

                if (!string.Equals(state.Status, ListingStatus.Active, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var expiresAt = state.ExpiresAt > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(state.ExpiresAt).UtcDateTime
                    : (data.ExpiresAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(data.ExpiresAt).UtcDateTime : now);
                if (expiresAt <= now)
                {
                    continue;
                }

                result.Add(ToQuartermasterListing(data, state, id));
            }

            await SaveCatalogueCache(result);
            logger.Info($"[TheQuartermaster] Loaded {result.Count} active listings from RTDB.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch active listings from RTDB: {ex.Message}", ex);
            result = await LoadCatalogueCache();
        }

        return result;
    }

    public async Task<int> GetActiveListingCountAsync()
    {
        if (!IsEnabled)
        {
            return 0;
        }

        try
        {
            var states = await GetDictionaryAsync<RtdbListingState>("listingStates");
            var listings = await GetDictionaryAsync<RtdbListing>("listings/available");
            if (states is null || states.Count == 0)
            {
                return 0;
            }

            var now = DateTime.UtcNow;
            var count = 0;
            foreach (var (id, state) in states)
            {
                if (!string.Equals(state.Status, ListingStatus.Active, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (state.ExpiresAt > 0 && DateTimeOffset.FromUnixTimeSeconds(state.ExpiresAt).UtcDateTime <= now)
                {
                    continue;
                }

                if (listings?.ContainsKey(id) == true)
                {
                    count++;
                }
            }

            return count;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to count active listings from RTDB: {ex.Message}", ex);
            return 0;
        }
    }

    public async Task<QuartermasterListing?> GetListingAsync(string listingId)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(listingId))
        {
            return null;
        }

        try
        {
            var data = await GetJsonAsync<RtdbListing>($"listings/available/{listingId}");
            if (data is null)
            {
                return null;
            }

            var state = await GetJsonAsync<RtdbListingState>($"listingStates/{listingId}");
            return ToQuartermasterListing(data, state, listingId);
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to get listing {listingId} from RTDB: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<int> TryPurchaseListingQuantityAsync(string listingId, string buyerProfileId, int quantity, string idempotencyKey)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(listingId) || quantity <= 0 || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return 0;
        }

        var buyerHash = HashProfileId(buyerProfileId);
        var buyerUid = firebaseAuthService.Uuid ?? buyerHash;

        for (var attempt = 0; attempt < MaxPurchaseRetries; attempt++)
        {
            try
            {
                var data = await GetJsonAsync<RtdbListing>($"listings/available/{listingId}");
                if (data is null)
                {
                    return 0;
                }

                var (state, etag) = await GetJsonWithEtagAsync<RtdbListingState>($"listingStates/{listingId}");
                state ??= new RtdbListingState
                {
                    Status = ListingStatus.Active,
                    CreatedAt = data.CreatedAt,
                    ExpiresAt = data.ExpiresAt,
                    RemainingQuantity = GetListingQuantity(data.ItemTreeJson)
                };

                if (!string.Equals(state.Status, ListingStatus.Active, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                var now = DateTime.UtcNow;
                var expiresAt = state.ExpiresAt > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(state.ExpiresAt).UtcDateTime
                    : DateTime.MaxValue;
                if (expiresAt <= now)
                {
                    return 0;
                }

                if (string.Equals(state.LastPurchaseId, idempotencyKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(state.LastPurchaseStatus, "completed", StringComparison.OrdinalIgnoreCase))
                    {
                        return 0;
                    }

                    if (state.LastPurchaseExpiresAt.HasValue && state.LastPurchaseExpiresAt.Value > new DateTimeOffset(now).ToUnixTimeSeconds())
                    {
                        return state.LastPurchaseQuantity;
                    }
                }

                var available = state.RemainingQuantity > 0 ? state.RemainingQuantity : GetListingQuantity(data.ItemTreeJson);
                if (available <= 0)
                {
                    return 0;
                }

                var toTake = Math.Min(quantity, available);
                var remaining = available - toTake;

                state.RemainingQuantity = remaining;
                state.LastPurchaseId = idempotencyKey;
                state.LastPurchaseQuantity = toTake;
                state.LastPurchaseStatus = "pending";
                state.LastPurchaseExpiresAt = new DateTimeOffset(now).AddMinutes(5).ToUnixTimeSeconds();

                if (remaining == 0)
                {
                    state.Status = ListingStatus.Sold;
                    state.BuyerUid = buyerUid;
                    state.BuyerHash = buyerHash;
                    state.SoldAt = new DateTimeOffset(now).ToUnixTimeSeconds();
                }
                else
                {
                    state.BuyerUid = null;
                    state.BuyerHash = null;
                    state.SoldAt = null;
                }

                if (await PutJsonWithEtagAsync($"listingStates/{listingId}", state, etag))
                {
                    logger.Debug($"[TheQuartermaster] Reserved {toTake} from listing {listingId} (attempt {attempt}).");
                    return toTake;
                }

                logger.Warning($"[TheQuartermaster] Listing {listingId} changed during purchase attempt {attempt}; retrying.");
                await Task.Delay(50);
            }
            catch (Exception ex)
            {
                logger.Error($"[TheQuartermaster] Failed to purchase quantity from listing {listingId}: {ex.Message}", ex);
                return 0;
            }
        }

        return 0;
    }

    public async Task CompleteListingPurchaseAsync(string listingId, string idempotencyKey)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(listingId) || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        try
        {
            var (state, etag) = await GetJsonWithEtagAsync<RtdbListingState>($"listingStates/{listingId}");
            if (state is null || !string.Equals(state.LastPurchaseId, idempotencyKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(state.LastPurchaseStatus, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            state.LastPurchaseStatus = "completed";

            if (!await PutJsonWithEtagAsync($"listingStates/{listingId}", state, etag))
            {
                logger.Warning($"[TheQuartermaster] Complete purchase for {listingId} conflicted; skipping.");
                return;
            }

            if (string.Equals(state.Status, ListingStatus.Sold, StringComparison.OrdinalIgnoreCase) && state.RemainingQuantity == 0)
            {
                var data = await GetJsonAsync<RtdbListing>($"listings/available/{listingId}");
                if (data is not null)
                {
                    await PutJsonAsync($"listings/sold/{listingId}", data);
                    await DeleteJsonAsync($"listings/available/{listingId}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to complete purchase for listing {listingId}: {ex.Message}", ex);
        }
    }

    public async Task ReleaseListingQuantityAsync(string listingId, string idempotencyKey)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(listingId) || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        try
        {
            var (state, etag) = await GetJsonWithEtagAsync<RtdbListingState>($"listingStates/{listingId}");
            if (state is null || !string.Equals(state.LastPurchaseId, idempotencyKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(state.LastPurchaseStatus, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var reservedQuantity = state.LastPurchaseQuantity;
            if (reservedQuantity <= 0)
            {
                return;
            }

            if (string.Equals(state.Status, ListingStatus.Sold, StringComparison.OrdinalIgnoreCase))
            {
                state.Status = ListingStatus.Active;
                state.RemainingQuantity = reservedQuantity;
                state.BuyerUid = null;
                state.BuyerHash = null;
                state.SoldAt = null;
            }
            else
            {
                state.RemainingQuantity += reservedQuantity;
            }

            state.LastPurchaseId = null;
            state.LastPurchaseQuantity = 0;
            state.LastPurchaseStatus = null;
            state.LastPurchaseExpiresAt = null;

            if (!await PutJsonWithEtagAsync($"listingStates/{listingId}", state, etag))
            {
                logger.Warning($"[TheQuartermaster] Release listing quantity for {listingId} conflicted; skipping.");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to release listing quantity for {listingId}: {ex.Message}", ex);
        }
    }

    public async Task CleanupExpiredListingsAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastExpiredCleanup < TimeSpan.FromMinutes(QuartermasterConstants.Timings.ExpiredCleanupIntervalMinutes))
        {
            return;
        }

        _lastExpiredCleanup = now;

        try
        {
            var states = await GetDictionaryAsync<RtdbListingState>("listingStates");
            if (states is null || states.Count == 0)
            {
                return;
            }

            var count = 0;
            foreach (var (id, state) in states)
            {
                if (!string.Equals(state.Status, ListingStatus.Active, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (state.ExpiresAt <= 0 || DateTimeOffset.FromUnixTimeSeconds(state.ExpiresAt).UtcDateTime > now)
                {
                    continue;
                }

                var (_, etag) = await GetJsonWithEtagAsync<RtdbListingState>($"listingStates/{id}");
                if (etag is null)
                {
                    continue;
                }

                state.Status = ListingStatus.Expired;
                if (await PutJsonWithEtagAsync($"listingStates/{id}", state, etag))
                {
                    count++;
                }

                if (count >= 100)
                {
                    break;
                }
            }

            if (count > 0)
            {
                logger.Info($"[TheQuartermaster] Marked {count} expired listings in RTDB.");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to cleanup expired listings in RTDB: {ex.Message}", ex);
        }
    }

    public async Task DeleteExpiredListingsAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var states = await GetDictionaryAsync<RtdbListingState>("listingStates");
            if (states is null || states.Count == 0)
            {
                return;
            }

            var count = 0;
            foreach (var (id, state) in states)
            {
                if (!string.Equals(state.Status, ListingStatus.Expired, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await DeleteJsonAsync($"listings/available/{id}");
                await DeleteJsonAsync($"listings/sold/{id}");
                await DeleteJsonAsync($"listingStates/{id}");
                count++;
            }

            if (count > 0)
            {
                logger.Info($"[TheQuartermaster] Deleted {count} expired listings from RTDB.");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to delete expired listings from RTDB: {ex.Message}", ex);
        }
    }

    public async Task RebuildCatalogueAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var listings = await GetActiveListingsAsync();
            var version = GenerateCatalogueVersion();
            var generatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var pageCount = (int)Math.Ceiling(listings.Count / (double)CataloguePageSize);

            if (pageCount == 0)
            {
                pageCount = 1;
            }

            var rtdbListings = listings.Select(ToRtdbListing).ToList();
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var pageListings = rtdbListings
                    .Skip(pageIndex * CataloguePageSize)
                    .Take(CataloguePageSize)
                    .ToList();

                var page = new RtdbCataloguePage
                {
                    PageId = $"page_{pageIndex:D3}",
                    CatalogueVersion = version,
                    GeneratedAt = generatedAt,
                    Listings = pageListings
                };

                await PutJsonAsync($"catalogue/versions/{version}/pages/{page.PageId}", page);
            }

            var meta = new RtdbCatalogueMeta
            {
                Version = version,
                GeneratedAt = generatedAt,
                PageCount = pageCount,
                ListingCount = listings.Count
            };

            await PutJsonAsync("meta/catalogue", meta);
            logger.Info($"[TheQuartermaster] Rebuilt catalogue version {version} with {listings.Count} listings across {pageCount} pages.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to rebuild catalogue: {ex.Message}", ex);
        }
    }

    public async Task<bool> TryAcquireCatalogueLeaseAsync(TimeSpan duration)
    {
        if (!IsEnabled)
        {
            return false;
        }

        try
        {
            var now = DateTime.UtcNow;
            var nowSeconds = new DateTimeOffset(now).ToUnixTimeSeconds();
            var expiresSeconds = new DateTimeOffset(now.Add(duration)).ToUnixTimeSeconds();
            var ownerUid = firebaseAuthService.Uuid ?? _instanceId;

            var (existing, etag) = await GetJsonWithEtagAsync<RtdbWorkerLease>("worker/catalogueLease");
            if (existing is not null && existing.ExpiresAt > nowSeconds && !string.Equals(existing.OwnerUid, ownerUid, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var lease = new RtdbWorkerLease
            {
                OwnerUid = ownerUid,
                AcquiredAt = nowSeconds,
                ExpiresAt = expiresSeconds
            };

            if (await PutJsonWithEtagAsync("worker/catalogueLease", lease, etag))
            {
                return true;
            }

            var verify = await GetJsonAsync<RtdbWorkerLease>("worker/catalogueLease");
            return verify is not null && string.Equals(verify.OwnerUid, ownerUid, StringComparison.OrdinalIgnoreCase) && verify.ExpiresAt > nowSeconds;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to acquire catalogue lease: {ex.Message}", ex);
            return false;
        }
    }

    public async Task ReleaseCatalogueLeaseAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var ownerUid = firebaseAuthService.Uuid ?? _instanceId;
            var existing = await GetJsonAsync<RtdbWorkerLease>("worker/catalogueLease");
            if (existing is not null && string.Equals(existing.OwnerUid, ownerUid, StringComparison.OrdinalIgnoreCase))
            {
                await DeleteJsonAsync("worker/catalogueLease");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to release catalogue lease: {ex.Message}", ex);
        }
    }

    public async Task UpdateCatalogueRebuildCountersAsync(int newCount, int soldCount)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var current = await GetJsonAsync<RtdbCatalogueRebuild>("worker/catalogueRebuild") ?? new RtdbCatalogueRebuild();
            current.PendingNewCount += newCount;
            current.PendingSoldCount += soldCount;
            current.LastRebuildAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await PutJsonAsync("worker/catalogueRebuild", current);
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to update catalogue rebuild counters: {ex.Message}", ex);
        }
    }

    private string ResolveDatabaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(configService.Config.FirebaseDatabaseUrl))
        {
            return configService.Config.FirebaseDatabaseUrl.TrimEnd('/') + '/';
        }

        var projectId = configService.Config.FirebaseProjectId;
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return string.Empty;
        }

        return $"https://{projectId}-default-rtdb.firebaseio.com/";
    }

    private async Task<string> BuildUrl(string path)
    {
        var baseUrl = ResolveDatabaseUrl();
        var token = await firebaseAuthService.GetIdTokenAsync();
        var auth = string.IsNullOrWhiteSpace(token) ? string.Empty : $"?auth={Uri.EscapeDataString(token)}";
        return $"{baseUrl}{RootPath}/{path}.json{auth}";
    }

    private async Task<T?> GetJsonAsync<T>(string path)
    {
        var url = await BuildUrl(path);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    private async Task<(T? Data, string? ETag)> GetJsonWithEtagAsync<T>(string path)
    {
        var url = await BuildUrl(path);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Firebase-ETag", "true");
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return (default, null);
        }

        var json = await response.Content.ReadAsStringAsync();
        var etag = response.Headers.ETag?.Tag;
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return (default, etag);
        }

        return (JsonSerializer.Deserialize<T>(json, _jsonOptions), etag);
    }

    private async Task<Dictionary<string, T>?> GetDictionaryAsync<T>(string path)
    {
        var url = await BuildUrl(path);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            logger.Warning($"[TheQuartermaster] RTDB read failed for {path}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            logger.Debug($"[TheQuartermaster] RTDB read returned empty for {path}.");
            return new Dictionary<string, T>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, T>>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to deserialize RTDB data at {path}: {ex.Message}", ex);
            return null;
        }
    }

    private async Task<bool> PutJsonAsync<T>(string path, T value)
    {
        var url = await BuildUrl(path);
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Content = new StringContent(JsonSerializer.Serialize(value, _jsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private async Task<bool> PutJsonWithEtagAsync<T>(string path, T value, string? etag)
    {
        var url = await BuildUrl(path);
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Content = new StringContent(JsonSerializer.Serialize(value, _jsonOptions), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(etag))
        {
            request.Headers.TryAddWithoutValidation("if-match", etag);
        }

        using var response = await _httpClient.SendAsync(request);
        return response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent;
    }

    private async Task<bool> DeleteJsonAsync(string path)
    {
        var url = await BuildUrl(path);
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        using var response = await _httpClient.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private RtdbListing ToRtdbListing(QuartermasterListing listing)
    {
        return new RtdbListing
        {
            Id = listing.Id,
            SellerHash = listing.SellerHash,
            SellerUid = listing.SellerUid,
            BuyerHash = listing.BuyerHash,
            BuyerUid = listing.BuyerUid,
            RootTpl = listing.RootTpl,
            RootName = listing.RootName,
            ShortName = listing.ShortName,
            ItemTreeJson = listing.ItemTreeJson,
            RequiredTpls = listing.RequiredTpls,
            BasePrice = listing.BasePrice,
            MarketPrice = listing.MarketPrice,
            QualityMultiplier = listing.QualityMultiplier,
            IsVanilla = listing.IsVanilla,
            CreatedAt = ToUnixSeconds(listing.CreatedAt),
            ExpiresAt = ToUnixSeconds(listing.ExpiresAt),
            SoldAt = ToUnixSeconds(listing.SoldAt),
            ServerId = listing.ServerId,
            Metadata = listing.Metadata,
            Status = listing.Status
        };
    }

    private RtdbListingState ToRtdbListingState(QuartermasterListing listing, int quantity)
    {
        return new RtdbListingState
        {
            Status = listing.Status,
            BuyerUid = listing.BuyerUid,
            BuyerHash = listing.BuyerHash,
            SoldAt = ToUnixSecondsNullable(listing.SoldAt),
            CreatedAt = ToUnixSeconds(listing.CreatedAt),
            ExpiresAt = ToUnixSeconds(listing.ExpiresAt),
            RemainingQuantity = quantity,
            LastPurchaseId = listing.LastPurchaseId,
            LastPurchaseQuantity = listing.LastPurchaseQuantity,
            LastPurchaseStatus = listing.LastPurchaseStatus,
            LastPurchaseExpiresAt = ToUnixSecondsNullable(listing.LastPurchaseExpiresAt)
        };
    }

    private QuartermasterListing ToQuartermasterListing(RtdbListing data, RtdbListingState? state, string id)
    {
        var remainingQuantity = state?.RemainingQuantity ?? GetListingQuantity(data.ItemTreeJson);
        var itemTreeJson = remainingQuantity > 0
            ? UpdateListingQuantity(data.ItemTreeJson, remainingQuantity)
            : data.ItemTreeJson;

        return new QuartermasterListing
        {
            Id = id,
            SellerHash = data.SellerHash,
            SellerUid = data.SellerUid,
            BuyerHash = state?.BuyerHash ?? data.BuyerHash,
            BuyerUid = state?.BuyerUid ?? data.BuyerUid,
            RootTpl = data.RootTpl,
            RootName = data.RootName,
            ShortName = data.ShortName,
            ItemTreeJson = itemTreeJson,
            RequiredTpls = data.RequiredTpls,
            BasePrice = data.BasePrice,
            MarketPrice = data.MarketPrice,
            QualityMultiplier = data.QualityMultiplier,
            IsVanilla = data.IsVanilla,
            Status = state?.Status ?? data.Status ?? ListingStatus.Active,
            CreatedAt = data.CreatedAt > 0 ? Timestamp.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(data.CreatedAt).UtcDateTime) : null,
            ExpiresAt = data.ExpiresAt > 0 ? Timestamp.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(data.ExpiresAt).UtcDateTime) : null,
            SoldAt = (state?.SoldAt ?? data.SoldAt) > 0 ? Timestamp.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(state?.SoldAt ?? data.SoldAt).UtcDateTime) : null,
            ServerId = data.ServerId,
            Metadata = data.Metadata,
            LastPurchaseId = state?.LastPurchaseId,
            LastPurchaseQuantity = state?.LastPurchaseQuantity ?? 0,
            LastPurchaseStatus = state?.LastPurchaseStatus,
            LastPurchaseExpiresAt = state?.LastPurchaseExpiresAt > 0 ? Timestamp.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(state.LastPurchaseExpiresAt.Value).UtcDateTime) : null
        };
    }

    private static int GetListingQuantity(string? itemTreeJson)
    {
        if (string.IsNullOrWhiteSpace(itemTreeJson))
        {
            return 1;
        }

        try
        {
            var node = JsonNode.Parse(itemTreeJson);
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return 1;
            }

            var stackCount = arr[0]?["upd"]?.AsObject()["StackObjectsCount"]?.GetValue<double?>();
            return stackCount.HasValue ? (int)stackCount.Value : 1;
        }
        catch
        {
            return 1;
        }
    }

    private static string? UpdateListingQuantity(string? itemTreeJson, int remaining)
    {
        if (string.IsNullOrWhiteSpace(itemTreeJson))
        {
            return itemTreeJson;
        }

        try
        {
            var node = JsonNode.Parse(itemTreeJson);
            if (node is not JsonArray arr || arr.Count == 0)
            {
                return itemTreeJson;
            }

            var upd = arr[0]!["upd"] ??= new JsonObject();
            upd["StackObjectsCount"] = remaining;
            return node.ToJsonString();
        }
        catch
        {
            return itemTreeJson;
        }
    }

    private static long ToUnixSeconds(Timestamp? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return 0;
        }

        return ((DateTimeOffset)timestamp.Value.ToDateTime()).ToUnixTimeSeconds();
    }

    private static long? ToUnixSecondsNullable(Timestamp? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return null;
        }

        return ((DateTimeOffset)timestamp.Value.ToDateTime()).ToUnixTimeSeconds();
    }

    private static string GenerateListingId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static string GenerateCatalogueVersion()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
    }

    private static string GetExpiryIndexPath(long expiresAtSeconds, string listingId)
    {
        var bucket = DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds).UtcDateTime.ToString("yyyy-MM-dd-HH");
        return $"expiryIndex/{bucket}/{listingId}";
    }

    private async Task SaveCatalogueCache(List<QuartermasterListing> listings)
    {
        try
        {
            var cacheDir = Path.Combine(configService.ModPath, "cache");
            Directory.CreateDirectory(cacheDir);

            var cache = new RtdbCatalogueCache
            {
                Version = GenerateCatalogueVersion(),
                GeneratedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                PageCount = (int)Math.Ceiling(listings.Count / (double)CataloguePageSize),
                Listings = listings.Select(ToRtdbListing).ToList(),
                States = new Dictionary<string, RtdbListingState>()
            };

            var path = Path.Combine(cacheDir, "catalogue.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(cache, _jsonOptions));
        }
        catch (Exception ex)
        {
            logger.Warning($"[TheQuartermaster] Failed to save catalogue cache: {ex.Message}");
        }
    }

    private async Task<List<QuartermasterListing>> LoadCatalogueCache()
    {
        var result = new List<QuartermasterListing>();
        try
        {
            var path = Path.Combine(configService.ModPath, "cache", "catalogue.json");
            if (!File.Exists(path))
            {
                return result;
            }

            var json = await File.ReadAllTextAsync(path);
            var cache = JsonSerializer.Deserialize<RtdbCatalogueCache>(json, _jsonOptions);
            if (cache?.Listings is null || cache.Listings.Count == 0)
            {
                return result;
            }

            foreach (var listing in cache.Listings)
            {
                cache.States.TryGetValue(listing.Id ?? string.Empty, out var state);
                result.Add(ToQuartermasterListing(listing, state, listing.Id ?? string.Empty));
            }

            logger.Info($"[TheQuartermaster] Fell back to local catalogue cache with {result.Count} listings.");
        }
        catch (Exception ex)
        {
            logger.Warning($"[TheQuartermaster] Failed to load catalogue cache: {ex.Message}");
        }

        return result;
    }

    private string HashProfileId(string profileId)
    {
        var salt = QuartermasterConstants.Seller.AnonymizationSalt;
        var input = string.IsNullOrEmpty(salt) ? profileId : $"{profileId}:{salt}";
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant();
        return hash[..Math.Min(hash.Length, 64)];
    }
}
