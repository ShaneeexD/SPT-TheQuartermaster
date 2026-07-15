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
    FirebaseAuthService firebaseAuthService,
    ListingConfigService listingConfigService,
    MarketplaceFileService marketplaceFileService
)
{
    private readonly HttpClient _httpClient = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private string _instanceId = $"{Environment.MachineName}-{Process.GetCurrentProcess().Id}-{Guid.NewGuid():N}";

    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _cacheInitialized;
    private string? _cachedVersion;
    private List<QuartermasterListing> _cachedListings = [];
    private RtdbListingLimits _listingLimits = new();

    private TimeSpan RefreshCooldown => TimeSpan.FromMinutes(listingConfigService.RefreshCooldownMinutes);
    private DateTime _lastRefreshTime = DateTime.MinValue;
    private RtdbCatalogueMeta? _lastCatalogueMeta;

    public bool IsEnabled { get; private set; }
    public bool IsCacheInitialized => _cacheInitialized;

    public List<QuartermasterListing> GetCachedActiveListings()
    {
        return _cachedListings.ToList();
    }

    public void AddListingToCache(QuartermasterListing listing)
    {
        if (!_cacheInitialized || string.IsNullOrWhiteSpace(listing.Id))
        {
            return;
        }

        _cachedListings.RemoveAll(l => l.Id == listing.Id);
        _cachedListings.Add(listing);
    }

    public void RemoveListingFromCache(string listingId)
    {
        if (!_cacheInitialized || string.IsNullOrWhiteSpace(listingId))
        {
            return;
        }

        _cachedListings.RemoveAll(l => l.Id == listingId);
    }

    public string InstanceId => _instanceId;

    private const string RootPath = "quartermaster";
    private const int MaxPurchaseRetries = 3;

    public async Task InitialiseAsync()
    {
        if (!configService.Config.ModEnabled)
        {
            IsEnabled = false;
            logger.DebugWarning("[TheQuartermaster] Realtime Database disabled (mod disabled).");
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
            logger.DebugInfo($"[TheQuartermaster] Realtime Database initialised for {ResolveDatabaseUrl()}.");

            await EnsureBuyFiltersAsync();
            await EnsureListingLimitsAsync();
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
            listing.ExpiresAt ??= Timestamp.FromDateTime(now.AddSeconds(listingConfigService.ListingDurationSeconds));

            var quantity = GetListingQuantity(listing.ItemTreeJson);
            var data = ToRtdbListing(listing);
            var state = ToRtdbListingState(listing, quantity);
            state.RemainingQuantity = quantity;
            state.Status = ListingStatus.Active;

            await PutJsonAsync($"listings/available/{listing.Id}", data);
            await PutJsonAsync($"listingStates/{listing.Id}", state);
            await BumpCatalogueVersionAsync();

            var result = ToQuartermasterListing(data, state, listing.Id);
            AddListingToCache(result);

            logger.DebugDebug($"[TheQuartermaster] Uploaded listing {listing.Id} to RTDB.");
            return result;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to upload listing to RTDB: {ex.Message}", ex);
            return null;
        }
    }

    private async Task<RtdbCatalogueMeta?> GetCatalogueMetaAsync()
    {
        return await GetJsonAsync<RtdbCatalogueMeta>("meta/catalogue");
    }

    public async Task<RtdbBuyFilters> GetBuyFiltersAsync()
    {
        if (!IsEnabled)
        {
            return new RtdbBuyFilters();
        }

        try
        {
            var filters = await GetJsonAsync<RtdbBuyFilters>("config/buyFilters");
            return filters ?? new RtdbBuyFilters();
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to load buy filters from RTDB: {ex.Message}", ex);
            return new RtdbBuyFilters();
        }
    }

    public async Task SaveBuyFiltersAsync(RtdbBuyFilters filters)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            await PutJsonAsync("config/buyFilters", filters);
            logger.DebugInfo("[TheQuartermaster] Saved buy filters to RTDB.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to save buy filters to RTDB: {ex.Message}", ex);
        }
    }

    public async Task EnsureBuyFiltersAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var existing = await GetJsonAsync<RtdbBuyFilters>("config/buyFilters");
            if (existing is null)
            {
                await PutJsonAsync("config/buyFilters", new RtdbBuyFilters());
                logger.DebugInfo("[TheQuartermaster] Seeded default buy filters in RTDB.");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to ensure buy filters in RTDB: {ex.Message}", ex);
        }
    }

    public RtdbListingLimits GetListingLimits()
    {
        return _listingLimits;
    }

    public async Task<RtdbListingLimits> GetListingLimitsAsync()
    {
        if (!IsEnabled)
        {
            return new RtdbListingLimits();
        }

        try
        {
            var limits = await GetJsonAsync<RtdbListingLimits>("config/listingLimits");
            if (limits is not null)
            {
                _listingLimits = limits;
            }

            return _listingLimits;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to load listing limits from RTDB: {ex.Message}", ex);
            return _listingLimits;
        }
    }

    public async Task SaveListingLimitsAsync(RtdbListingLimits limits)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            await PutJsonAsync("config/listingLimits", limits);
            _listingLimits = limits;
            logger.DebugInfo("[TheQuartermaster] Saved listing limits to RTDB.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to save listing limits to RTDB: {ex.Message}", ex);
        }
    }

    public async Task EnsureListingLimitsAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var existing = await GetJsonAsync<RtdbListingLimits>("config/listingLimits");
            if (existing is null)
            {
                await PutJsonAsync("config/listingLimits", _listingLimits);
                logger.DebugInfo("[TheQuartermaster] Seeded default listing limits in RTDB.");
            }
            else
            {
                _listingLimits = existing;
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to ensure listing limits in RTDB: {ex.Message}", ex);
        }
    }

    public async Task BumpCatalogueVersionAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        const int MaxRetries = 3;
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var (meta, etag) = await GetJsonWithEtagAsync<RtdbCatalogueMeta>("meta/catalogue");
                meta ??= new RtdbCatalogueMeta();
                meta.Version = GetNextCatalogueVersion(meta);
                meta.GeneratedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                if (!await PutJsonWithEtagAsync("meta/catalogue", meta, etag))
                {
                    logger.DebugDebug($"[TheQuartermaster] Catalogue version bump conflicted (attempt {attempt + 1}); retrying.");
                    await Task.Delay(50);
                    continue;
                }

                logger.DebugDebug($"[TheQuartermaster] Bumped catalogue version to {meta.Version}.");
                return;
            }
            catch (Exception ex)
            {
                logger.Error($"[TheQuartermaster] Failed to bump catalogue version: {ex.Message}", ex);
                return;
            }
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
            var wasInitialized = _cacheInitialized;
            var (listings, refreshed, _) = await RefreshActiveListingsIfNeededAsync();
            var versionText = _cachedVersion ?? "(none)";
            if (refreshed)
            {
                logger.DebugInfo($"[TheQuartermaster] Catalogue version {versionText} updated; loaded {listings.Count} active listings.");
            }
            else if (!wasInitialized)
            {
                logger.DebugInfo($"[TheQuartermaster] Catalogue version {versionText} unchanged; using {listings.Count} local cache listings.");
            }
            else
            {
                logger.DebugDebug($"[TheQuartermaster] Catalogue version {versionText} unchanged; using {listings.Count} local cache listings.");
            }
            return listings;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to refresh active listings from RTDB: {ex.Message}", ex);
            return await LoadCatalogueCache();
        }
    }

    private async Task<(List<QuartermasterListing> Listings, bool Refreshed, RtdbCatalogueMeta? Meta)> RefreshActiveListingsIfNeededAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            if (_lastRefreshTime == DateTime.MinValue)
            {
                var lastRefresh = await TryLoadLastRefreshAsync();
                if (lastRefresh.HasValue)
                {
                    _lastRefreshTime = lastRefresh.Value;
                }
            }

            // Try marketplace file service first (bypasses cooldown - has its own cache)
            if (marketplaceFileService.IsEnabled)
            {
                try
                {
                    var bundle = await marketplaceFileService.TryGetBundleAsync();
                    if (bundle is not null && bundle.Listings is not null)
                    {
                        var fileVersion = bundle.Version;
                        if (_cacheInitialized && _cachedVersion == fileVersion)
                        {
                            _lastRefreshTime = DateTime.UtcNow;
                            await SaveLastRefreshAsync();
                            return (_cachedListings.ToList(), false, _lastCatalogueMeta);
                        }

                        var fileStates = bundle.States ?? new Dictionary<string, RtdbListingState>();
                        var fileListings = BuildListingsFromData(bundle.Listings, fileStates);
                        _cachedListings = fileListings;
                        _cachedVersion = fileVersion;
                        _cacheInitialized = true;
                        _lastRefreshTime = DateTime.UtcNow;
                        await SaveLastRefreshAsync();
                        await SaveCatalogueCache(fileListings, fileVersion);
                        logger.DebugInfo($"[TheQuartermaster] Loaded {fileListings.Count} listings from marketplace file (version={fileVersion}).");
                        return (fileListings, true, null);
                    }
                }
                catch (Exception ex)
                {
                    logger.DebugWarning($"[TheQuartermaster] Marketplace file service failed, falling back to RTDB: {ex.Message}");
                }
            }

            // Cooldown only applies to direct RTDB reads (fallback path)
            if (DateTime.UtcNow - _lastRefreshTime < RefreshCooldown)
            {
                var remaining = RefreshCooldown - (DateTime.UtcNow - _lastRefreshTime);
                logger.DebugInfo($"[TheQuartermaster] RTDB refresh skipped (cooldown). {remaining.TotalMinutes:F1} minutes until next refresh allowed.");

                if (_cacheInitialized)
                {
                    return (_cachedListings.ToList(), false, _lastCatalogueMeta);
                }

                var localCache = await TryLoadLocalCacheAsync();
                if (localCache is not null)
                {
                    var cachedListings = localCache.Listings
                        .Select(l => ToQuartermasterListing(l, null, l.Id ?? string.Empty))
                        .ToList();
                    _cachedListings = cachedListings;
                    _cachedVersion = localCache.Version;
                    _cacheInitialized = true;
                    logger.DebugInfo("[TheQuartermaster] Catalogue cache is within cooldown; loaded from local cache.");
                    return (cachedListings, false, _lastCatalogueMeta);
                }
            }

            var meta = await GetCatalogueMetaAsync();
            _lastCatalogueMeta = meta;
            var remoteVersion = meta?.Version;
            if (_cacheInitialized && _cachedVersion == remoteVersion)
            {
                _lastRefreshTime = DateTime.UtcNow;
                await SaveLastRefreshAsync();
                return (_cachedListings.ToList(), false, meta);
            }

            if (!_cacheInitialized && !string.IsNullOrWhiteSpace(remoteVersion))
            {
                var localCache = await TryLoadLocalCacheAsync();
                if (localCache is not null && localCache.Version == remoteVersion)
                {
                    var cachedListings = localCache.Listings
                        .Select(l => ToQuartermasterListing(l, null, l.Id ?? string.Empty))
                        .ToList();
                    _cachedListings = cachedListings;
                    _cachedVersion = remoteVersion;
                    _cacheInitialized = true;
                    _lastRefreshTime = DateTime.UtcNow;
                    await SaveLastRefreshAsync();
                    return (cachedListings, false, meta);
                }
            }

            var listings = await LoadActiveListingsFromRtdbAsync();
            _cachedListings = listings;
            _cachedVersion = remoteVersion;
            _cacheInitialized = true;
            _lastRefreshTime = DateTime.UtcNow;
            await SaveLastRefreshAsync();
            await SaveCatalogueCache(listings, remoteVersion);
            return (listings, true, meta);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<List<QuartermasterListing>> LoadActiveListingsFromRtdbAsync()
    {
        var result = new List<QuartermasterListing>();

        var listingsTask = GetDictionaryAsync<RtdbListing>("listings/available");
        var statesTask = GetDictionaryAsync<RtdbListingState>("listingStates");
        await Task.WhenAll(listingsTask, statesTask);

        var listings = await listingsTask;
        Dictionary<string, RtdbListingState> states = await statesTask ?? new Dictionary<string, RtdbListingState>();

        logger.DebugDebug($"[TheQuartermaster] RTDB raw listings count: {listings?.Count ?? 0}, raw states count: {states?.Count ?? 0}.");

        if (listings is null)
        {
            logger.DebugInfo("[TheQuartermaster] No listings returned from RTDB; trader will have 0 items.");
            return result;
        }

        return BuildListingsFromData(listings, states ?? new Dictionary<string, RtdbListingState>());
    }

    private List<QuartermasterListing> BuildListingsFromData(Dictionary<string, RtdbListing> listings, Dictionary<string, RtdbListingState> states)
    {
        var result = new List<QuartermasterListing>();

        if (listings.Count == 0)
        {
            logger.DebugInfo("[TheQuartermaster] No active listings in data; trader will have 0 items.");
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
                    logger.DebugDebug($"[TheQuartermaster] Reserved {toTake} from listing {listingId} (attempt {attempt}).");
                    return toTake;
                }

                logger.DebugWarning($"[TheQuartermaster] Listing {listingId} changed during purchase attempt {attempt}; retrying.");
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
                logger.DebugWarning($"[TheQuartermaster] Complete purchase for {listingId} conflicted; skipping.");
                return;
            }

            if (string.Equals(state.Status, ListingStatus.Sold, StringComparison.OrdinalIgnoreCase) && state.RemainingQuantity == 0)
            {
                var data = await GetJsonAsync<RtdbListing>($"listings/available/{listingId}");
                if (data is not null)
                {
                    data.SoldAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await PutJsonAsync($"listings/sold/{listingId}", data);
                    await DeleteJsonAsync($"listings/available/{listingId}");
                }

                RemoveListingFromCache(listingId);
            }

            await BumpCatalogueVersionAsync();
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
                logger.DebugWarning($"[TheQuartermaster] Release listing quantity for {listingId} conflicted; skipping.");
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

        try
        {
            var states = await GetDictionaryAsync<RtdbListingState>("listingStates");
            if (states is null || states.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var count = 0;
            var staleFound = 0;
            var etagNullSkips = 0;
            var putFailures = 0;
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

                staleFound++;

                var (_, etag) = await GetJsonWithEtagAsync<RtdbListingState>($"listingStates/{id}");
                if (etag is null)
                {
                    etagNullSkips++;
                    continue;
                }

                state.Status = ListingStatus.Expired;
                if (await PutJsonWithEtagAsync($"listingStates/{id}", state, etag))
                {
                    count++;
                }
                else
                {
                    putFailures++;
                }

                if (count >= 200)
                {
                    break;
                }
            }

            logger.DebugInfo($"[TheQuartermaster] Expiry scan: {states.Count} states, {staleFound} stale-active found, {count} marked, {etagNullSkips} etag-null skips, {putFailures} put failures.");

            if (count > 0)
            {
                logger.DebugInfo($"[TheQuartermaster] Marked {count} expired listings in RTDB.");
                await BumpCatalogueVersionAsync();
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

            var available = await GetDictionaryAsync<RtdbListing>("listings/available");
            var sold = await GetDictionaryAsync<RtdbListing>("listings/sold");
            var availableIds = (available?.Keys ?? new Dictionary<string, RtdbListing>().Keys).ToHashSet();
            var soldIds = (sold?.Keys ?? new Dictionary<string, RtdbListing>().Keys).ToHashSet();

            var now = DateTime.UtcNow;
            var count = 0;
            var orphanCount = 0;
            foreach (var (id, state) in states)
            {
                var shouldDelete = string.Equals(state.Status, ListingStatus.Expired, StringComparison.OrdinalIgnoreCase);

                if (!shouldDelete && string.Equals(state.Status, ListingStatus.Active, StringComparison.OrdinalIgnoreCase))
                {
                    if (state.ExpiresAt > 0 && DateTimeOffset.FromUnixTimeSeconds(state.ExpiresAt).UtcDateTime <= now)
                    {
                        shouldDelete = true;
                    }
                }

                if (!shouldDelete && string.Equals(state.Status, ListingStatus.Sold, StringComparison.OrdinalIgnoreCase))
                {
                    if (state.ExpiresAt > 0 && DateTimeOffset.FromUnixTimeSeconds(state.ExpiresAt).UtcDateTime <= now)
                    {
                        shouldDelete = true;
                    }
                }

                var hasListing = availableIds.Contains(id) || soldIds.Contains(id);

                if (shouldDelete)
                {
                    await DeleteJsonAsync($"listings/available/{id}");
                    await DeleteJsonAsync($"listings/sold/{id}");
                    await DeleteJsonAsync($"listingStates/{id}");
                    count++;
                }
                else if (!hasListing)
                {
                    await DeleteJsonAsync($"listingStates/{id}");
                    orphanCount++;
                }

                if (count + orphanCount >= 1000)
                {
                    break;
                }
            }

            if (count > 0 || orphanCount > 0)
            {
                logger.DebugInfo($"[TheQuartermaster] Deleted {count} expired/sold listings and {orphanCount} orphan states from RTDB.");
                await BumpCatalogueVersionAsync();
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to delete expired listings from RTDB: {ex.Message}", ex);
        }
    }

    public async Task CleanupSoldListingsAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var soldListings = await GetDictionaryAsync<RtdbListing>("listings/sold") ?? new Dictionary<string, RtdbListing>();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var cutoff = now - 300; // 5 minutes ago

            var count = 0;
            foreach (var (id, listing) in soldListings)
            {
                if (listing is null)
                {
                    continue;
                }

                var soldAt = listing.SoldAt;
                if (soldAt <= 0)
                {
                    soldAt = listing.CreatedAt;
                }
                if (soldAt <= 0)
                {
                    soldAt = listing.ExpiresAt;
                }

                if (soldAt > 0 && soldAt <= cutoff)
                {
                    await DeleteJsonAsync($"listings/sold/{id}");
                    await DeleteJsonAsync($"listingStates/{id}");
                    count++;
                }
            }

            if (count > 0)
            {
                logger.DebugInfo($"[TheQuartermaster] Cleaned up {count} sold listings older than 5 minutes.");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to cleanup sold listings: {ex.Message}", ex);
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
            var (listings, refreshed, meta) = await RefreshActiveListingsIfNeededAsync();
            if (!refreshed)
            {
                logger.DebugInfo("[TheQuartermaster] Catalogue unchanged; no rebuild needed.");
                return;
            }

            var version = meta?.Version ?? _cachedVersion;
            if (string.IsNullOrWhiteSpace(version))
            {
                version = GetNextCatalogueVersion(meta);
            }

            if (meta is null)
            {
                logger.DebugInfo("[TheQuartermaster] No catalogue meta found; building initial catalogue.");
            }

            _cachedVersion = version;
            var generatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var newMeta = new RtdbCatalogueMeta
            {
                Version = version,
                GeneratedAt = generatedAt,
                PageCount = 1,
                ListingCount = listings.Count
            };

            await PutJsonAsync("meta/catalogue", newMeta);
            await SaveCatalogueCache(listings, version);
            logger.DebugInfo($"[TheQuartermaster] Rebuilt catalogue version {version} with {listings.Count} listings.");
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
        if (string.IsNullOrWhiteSpace(etag) && response.Headers.TryGetValues("ETag", out var etagValues))
        {
            etag = etagValues.FirstOrDefault();
        }
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
            logger.DebugWarning($"[TheQuartermaster] RTDB read failed for {path}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            logger.DebugDebug($"[TheQuartermaster] RTDB read returned empty for {path}.");
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
        if (!response.IsSuccessStatusCode)
        {
            logger.DebugWarning($"[TheQuartermaster] RTDB write failed for {path}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
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

    public static int GetListingQuantity(string? itemTreeJson)
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

    private static string GetNextCatalogueVersion(RtdbCatalogueMeta? currentMeta)
    {
        const int MaxVersion = 9999999;
        var current = 0;
        if (currentMeta is not null && !string.IsNullOrWhiteSpace(currentMeta.Version))
        {
            int.TryParse(currentMeta.Version, out current);
        }

        var next = current + 1;
        if (next > MaxVersion)
        {
            next = 1;
        }

        return next.ToString();
    }

    private async Task SaveCatalogueCache(List<QuartermasterListing> listings, string? version)
    {
        try
        {
            var cacheDir = Path.Combine(configService.ModPath, "cache");
            Directory.CreateDirectory(cacheDir);

            var cache = new RtdbCatalogueCache
            {
                Version = version,
                GeneratedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                PageCount = 1,
                Listings = listings.Select(ToRtdbListing).ToList(),
                States = new Dictionary<string, RtdbListingState>()
            };

            var path = Path.Combine(cacheDir, "catalogue.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(cache, _jsonOptions));
        }
        catch (Exception ex)
        {
            logger.DebugWarning($"[TheQuartermaster] Failed to save catalogue cache: {ex.Message}");
        }
    }

    private async Task<RtdbCatalogueCache?> TryLoadLocalCacheAsync()
    {
        try
        {
            var path = Path.Combine(configService.ModPath, "cache", "catalogue.json");
            if (!File.Exists(path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(path);
            var cache = JsonSerializer.Deserialize<RtdbCatalogueCache>(json, _jsonOptions);
            if (cache?.Listings is null || cache.Listings.Count == 0)
            {
                return null;
            }

            return cache;
        }
        catch (Exception ex)
        {
            logger.DebugDebug($"[TheQuartermaster] Failed to load local catalogue cache: {ex.Message}");
            return null;
        }
    }

    private async Task<List<QuartermasterListing>> LoadCatalogueCache()
    {
        var result = new List<QuartermasterListing>();
        try
        {
            var cache = await TryLoadLocalCacheAsync();
            if (cache is null)
            {
                return result;
            }

            foreach (var listing in cache.Listings)
            {
                cache.States.TryGetValue(listing.Id ?? string.Empty, out var state);
                result.Add(ToQuartermasterListing(listing, state, listing.Id ?? string.Empty));
            }

            await _cacheLock.WaitAsync();
            try
            {
                _cachedListings = result.ToList();
                _cachedVersion = cache.Version;
                _cacheInitialized = true;
                _lastRefreshTime = DateTime.UtcNow;
                await SaveLastRefreshAsync();
            }
            finally
            {
                _cacheLock.Release();
            }

            logger.DebugInfo($"[TheQuartermaster] Fell back to local catalogue cache with {result.Count} listings.");
        }
        catch (Exception ex)
        {
            logger.DebugWarning($"[TheQuartermaster] Failed to load catalogue cache: {ex.Message}");
        }

        return result;
    }

    private class CacheRefreshData
    {
        [JsonPropertyName("last_refresh")]
        public long LastRefresh { get; set; }
    }

    private async Task SaveLastRefreshAsync()
    {
        try
        {
            var cacheDir = Path.Combine(configService.ModPath, "cache");
            Directory.CreateDirectory(cacheDir);

            var data = new CacheRefreshData
            {
                LastRefresh = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var path = Path.Combine(cacheDir, "lastRefresh.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, _jsonOptions));
        }
        catch (Exception ex)
        {
            logger.DebugWarning($"[TheQuartermaster] Failed to save last refresh time: {ex.Message}");
        }
    }

    private async Task<DateTime?> TryLoadLastRefreshAsync()
    {
        try
        {
            var path = Path.Combine(configService.ModPath, "cache", "lastRefresh.json");
            if (!File.Exists(path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(path);
            var data = JsonSerializer.Deserialize<CacheRefreshData>(json, _jsonOptions);
            if (data is null)
            {
                return null;
            }

            return DateTimeOffset.FromUnixTimeSeconds(data.LastRefresh).UtcDateTime;
        }
        catch (Exception ex)
        {
            logger.DebugDebug($"[TheQuartermaster] Failed to load last refresh time: {ex.Message}");
            return null;
        }
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
