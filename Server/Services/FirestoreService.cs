using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Grpc.Core;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class FirestoreService(
    ISptLogger<FirestoreService> logger,
    ConfigService configService,
    FirebaseAuthService firebaseAuthService
)
{
    private FirestoreDb? _db;
    private DateTime _lastExpiredCleanup = DateTime.MinValue;
    public bool IsEnabled { get; private set; }
    public FirestoreDb? Db => _db;

    public async Task InitialiseAsync()
    {
        if (!configService.Config.ModEnabled)
        {
            IsEnabled = false;
            logger.Warning("[TheQuartermaster] Firestore disabled (mod disabled).");
            return;
        }

        var projectId = configService.Config.FirebaseProjectId;
        var apiKey = configService.Config.FirebaseApiKey;

        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(apiKey))
        {
            IsEnabled = false;
            logger.Error("[TheQuartermaster] Firestore public config missing project_id/api_key.");
            return;
        }

        try
        {
            await firebaseAuthService.InitialiseAsync();

            var firestoreClient = await new FirestoreClientBuilder
            {
                ChannelCredentials = ChannelCredentials.SecureSsl
            }.BuildAsync();

            _db = FirestoreDb.Create(projectId, firestoreClient);
            IsEnabled = true;
            logger.Info($"[TheQuartermaster] Firestore initialised for project {projectId} with no credentials (open rules).");
        }
        catch (Exception ex)
        {
            IsEnabled = false;
            logger.Error($"[TheQuartermaster] Firestore initialisation failed: {ex.Message}", ex);
        }
    }

    public async Task<QuartermasterListing?> UploadListingAsync(QuartermasterListing listing)
    {
        if (!IsEnabled || _db is null)
        {
            return null;
        }

        try
        {
            listing.SellerUid ??= firebaseAuthService.Uuid;
            var collection = _db.Collection(QuartermasterConstants.FirestoreCollections.Listings);
            var reference = await collection.AddAsync(listing);
            listing.Id = reference.Id;
            logger.Debug($"[TheQuartermaster] Uploaded listing {reference.Id}");
            return listing;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to upload listing: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<List<QuartermasterListing>> GetActiveListingsAsync()
    {
        var result = new List<QuartermasterListing>();
        if (!IsEnabled || _db is null)
        {
            return result;
        }

        try
        {
            var collection = _db.Collection(QuartermasterConstants.FirestoreCollections.Listings);
            var query = collection
                .WhereEqualTo("status", ListingStatus.Active)
                .WhereGreaterThan("expires_at", Timestamp.GetCurrentTimestamp());

            var snapshot = await query.GetSnapshotAsync();
            foreach (var doc in snapshot.Documents)
            {
                var listing = doc.ConvertTo<QuartermasterListing>();
                listing.Id = doc.Id;
                result.Add(listing);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch active listings: {ex.Message}", ex);
        }

        return result;
    }

    public async Task<int> GetActiveListingCountAsync()
    {
        if (!IsEnabled || _db is null)
        {
            return 0;
        }

        try
        {
            var query = _db.Collection(QuartermasterConstants.FirestoreCollections.Listings)
                .WhereEqualTo("status", ListingStatus.Active);

            var snapshot = await query.Count().GetSnapshotAsync();
            return (int)(snapshot.Count ?? 0);
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to count active listings: {ex.Message}", ex);
            return 0;
        }
    }

    public async Task<QuartermasterListing?> GetListingAsync(string listingId)
    {
        if (!IsEnabled || _db is null || string.IsNullOrWhiteSpace(listingId))
        {
            return null;
        }

        try
        {
            var docRef = _db.Collection(QuartermasterConstants.FirestoreCollections.Listings).Document(listingId);
            var snapshot = await docRef.GetSnapshotAsync();
            if (!snapshot.Exists)
            {
                return null;
            }

            var listing = snapshot.ConvertTo<QuartermasterListing>();
            listing.Id = snapshot.Id;
            return listing;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to get listing {listingId}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<QuartermasterListing?> TryPurchaseListingAsync(string listingId, string buyerProfileId)
    {
        if (!IsEnabled || _db is null || string.IsNullOrWhiteSpace(listingId))
        {
            return null;
        }

        try
        {
            var docRef = _db.Collection(QuartermasterConstants.FirestoreCollections.Listings).Document(listingId);
            var buyerHash = HashProfileId(buyerProfileId);
            var buyerUid = firebaseAuthService.Uuid ?? buyerHash;

            var result = await _db.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(docRef);
                if (!snapshot.Exists)
                {
                    return null;
                }

                var listing = snapshot.ConvertTo<QuartermasterListing>();
                listing.Id = snapshot.Id;

                if (listing.Status != ListingStatus.Active)
                {
                    return null;
                }

                if (listing.ExpiresAt is not null && listing.ExpiresAt.Value.ToDateTime() < DateTime.UtcNow)
                {
                    return null;
                }

                listing.Status = ListingStatus.Sold;
                listing.BuyerHash = buyerHash;
                listing.BuyerUid = buyerUid;
                listing.SoldAt = Timestamp.GetCurrentTimestamp();

                transaction.Set(docRef, listing);
                return listing;
            });

            return result;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to purchase listing {listingId}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task CleanupExpiredListingsAsync()
    {
        if (!IsEnabled || _db is null)
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
            var query = _db.Collection(QuartermasterConstants.FirestoreCollections.Listings)
                .WhereEqualTo("status", ListingStatus.Active)
                .WhereLessThanOrEqualTo("expires_at", Timestamp.GetCurrentTimestamp())
                .Limit(100);

            var snapshot = await query.GetSnapshotAsync();
            var batch = _db.StartBatch();
            var count = 0;
            foreach (var doc in snapshot.Documents)
            {
                batch.Update(doc.Reference, "status", ListingStatus.Expired);
                count++;
            }

            if (count > 0)
            {
                await batch.CommitAsync();
                logger.Info($"[TheQuartermaster] Marked {count} expired listings as expired.");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to cleanup expired listings: {ex.Message}", ex);
        }
    }

    public async Task DeleteExpiredListingsAsync()
    {
        if (!IsEnabled || _db is null)
        {
            return;
        }

        const int batchSize = 500;
        var totalDeleted = 0;

        try
        {
            while (true)
            {
                var snapshot = await _db.Collection(QuartermasterConstants.FirestoreCollections.Listings)
                    .WhereEqualTo("status", ListingStatus.Expired)
                    .Limit(batchSize)
                    .GetSnapshotAsync();

                if (snapshot.Count == 0)
                {
                    break;
                }

                var batch = _db.StartBatch();
                foreach (var doc in snapshot.Documents)
                {
                    batch.Delete(doc.Reference);
                }

                await batch.CommitAsync();
                totalDeleted += snapshot.Count;

                if (snapshot.Count < batchSize)
                {
                    break;
                }
            }

            if (totalDeleted > 0)
            {
                logger.Info($"[TheQuartermaster] Deleted {totalDeleted} expired listing documents from Firestore.");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to delete expired listings: {ex.Message}", ex);
        }
    }

    public async Task<int> TryPurchaseListingQuantityAsync(string listingId, string buyerProfileId, int quantity, string idempotencyKey)
    {
        if (_db is null || string.IsNullOrWhiteSpace(listingId) || quantity <= 0 || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return 0;
        }

        var docRef = _db.Collection(QuartermasterConstants.FirestoreCollections.Listings).Document(listingId);
        var buyerHash = HashProfileId(buyerProfileId);
        var buyerUid = firebaseAuthService.Uuid ?? buyerHash;

        try
        {
            var result = await _db.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(docRef);
                if (!snapshot.Exists)
                {
                    return 0;
                }

                var listing = snapshot.ConvertTo<QuartermasterListing>();
                listing.Id = snapshot.Id;

                if (listing.Status != ListingStatus.Active)
                {
                    return 0;
                }

                if (listing.ExpiresAt is not null && listing.ExpiresAt.Value.ToDateTime() < DateTime.UtcNow)
                {
                    return 0;
                }

                var now = DateTime.UtcNow;
                if (string.Equals(listing.LastPurchaseId, idempotencyKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(listing.LastPurchaseStatus, "completed", StringComparison.OrdinalIgnoreCase))
                    {
                        return 0;
                    }

                    if (listing.LastPurchaseExpiresAt.HasValue && listing.LastPurchaseExpiresAt.Value.ToDateTime() >= now)
                    {
                        return listing.LastPurchaseQuantity;
                    }
                }

                var available = GetListingQuantity(listing.ItemTreeJson);
                if (available <= 0)
                {
                    return 0;
                }

                var toTake = Math.Min(quantity, available);
                var remaining = available - toTake;

                if (remaining == 0)
                {
                    listing.Status = ListingStatus.Sold;
                    listing.BuyerHash = buyerHash;
                    listing.BuyerUid = buyerUid;
                    listing.SoldAt = Timestamp.GetCurrentTimestamp();
                }
                else
                {
                    listing.ItemTreeJson = UpdateListingQuantity(listing.ItemTreeJson, remaining);
                }

                listing.LastPurchaseId = idempotencyKey;
                listing.LastPurchaseQuantity = toTake;
                listing.LastPurchaseStatus = "pending";
                listing.LastPurchaseExpiresAt = Timestamp.FromDateTime(now.AddMinutes(5));

                transaction.Set(docRef, listing);
                return toTake;
            });

            return result;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to purchase quantity from listing {listingId}: {ex.Message}", ex);
            return 0;
        }
    }

    public async Task CompleteListingPurchaseAsync(string listingId, string idempotencyKey)
    {
        if (_db is null || string.IsNullOrWhiteSpace(listingId) || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        var docRef = _db.Collection(QuartermasterConstants.FirestoreCollections.Listings).Document(listingId);

        try
        {
            await _db.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(docRef);
                if (!snapshot.Exists)
                {
                    return;
                }

                var listing = snapshot.ConvertTo<QuartermasterListing>();
                if (!string.Equals(listing.LastPurchaseId, idempotencyKey, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                listing.LastPurchaseStatus = "completed";
                transaction.Set(docRef, listing);
            });
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to complete purchase for listing {listingId}: {ex.Message}", ex);
        }
    }

    public async Task ReleaseListingQuantityAsync(string listingId, string idempotencyKey)
    {
        if (_db is null || string.IsNullOrWhiteSpace(listingId) || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        var docRef = _db.Collection(QuartermasterConstants.FirestoreCollections.Listings).Document(listingId);

        try
        {
            await _db.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(docRef);
                if (!snapshot.Exists)
                {
                    return;
                }

                var listing = snapshot.ConvertTo<QuartermasterListing>();
                if (!string.Equals(listing.LastPurchaseId, idempotencyKey, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (string.Equals(listing.LastPurchaseStatus, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var reservedQuantity = listing.LastPurchaseQuantity;
                if (reservedQuantity <= 0)
                {
                    return;
                }

                if (listing.Status == ListingStatus.Sold)
                {
                    listing.Status = ListingStatus.Active;
                    listing.BuyerHash = null;
                    listing.BuyerUid = null;
                    listing.SoldAt = null;
                    listing.ItemTreeJson = UpdateListingQuantity(listing.ItemTreeJson, reservedQuantity);
                }
                else
                {
                    var currentQuantity = GetListingQuantity(listing.ItemTreeJson);
                    listing.ItemTreeJson = UpdateListingQuantity(listing.ItemTreeJson, currentQuantity + reservedQuantity);
                }

                listing.LastPurchaseId = null;
                listing.LastPurchaseQuantity = 0;
                listing.LastPurchaseStatus = null;
                listing.LastPurchaseExpiresAt = null;

                transaction.Set(docRef, listing);
            });
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to release listing quantity for {listingId}: {ex.Message}", ex);
        }
    }

    private static int GetListingQuantity(string? itemTreeJson)
    {
        if (string.IsNullOrWhiteSpace(itemTreeJson))
        {
            return 1;
        }

        var node = JsonNode.Parse(itemTreeJson);
        if (node is not JsonArray arr || arr.Count == 0)
        {
            return 1;
        }

        var stackCount = arr[0]?["upd"]?.AsObject()["StackObjectsCount"]?.GetValue<double?>();
        return stackCount.HasValue ? (int)stackCount.Value : 1;
    }

    private static string? UpdateListingQuantity(string? itemTreeJson, int remaining)
    {
        if (string.IsNullOrWhiteSpace(itemTreeJson))
        {
            return itemTreeJson;
        }

        var node = JsonNode.Parse(itemTreeJson);
        if (node is not JsonArray arr || arr.Count == 0)
        {
            return itemTreeJson;
        }

        var upd = arr[0]!["upd"] ??= new JsonObject();
        upd["StackObjectsCount"] = remaining;
        return node.ToJsonString();
    }

    private string HashProfileId(string profileId)
    {
        var salt = QuartermasterConstants.Seller.AnonymizationSalt;
        var input = string.IsNullOrEmpty(salt) ? profileId : $"{profileId}:{salt}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant();
        return hash[..Math.Min(hash.Length, 64)];
    }
}
