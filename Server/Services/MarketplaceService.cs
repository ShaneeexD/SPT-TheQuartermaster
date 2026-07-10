using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class MarketplaceService(
    ConfigService configService,
    FirestoreService firestoreService,
    RealtimeDatabaseService realtimeDatabaseService
)
{
    public bool IsEnabled => UseRealtimeDatabase
        ? realtimeDatabaseService.IsEnabled
        : firestoreService.IsEnabled;

    private bool UseRealtimeDatabase =>
        string.Equals(configService.Config.MarketplaceStorage, "realtimeDatabase", StringComparison.OrdinalIgnoreCase);

    public async Task InitialiseAsync()
    {
        if (UseRealtimeDatabase)
        {
            await realtimeDatabaseService.InitialiseAsync();
        }
    }

    public async Task<QuartermasterListing?> UploadListingAsync(QuartermasterListing listing)
    {
        if (!IsEnabled)
        {
            return null;
        }

        return UseRealtimeDatabase
            ? await realtimeDatabaseService.UploadListingAsync(listing)
            : await firestoreService.UploadListingAsync(listing);
    }

    public async Task<List<QuartermasterListing>> GetActiveListingsAsync()
    {
        if (!IsEnabled)
        {
            return [];
        }

        return UseRealtimeDatabase
            ? await realtimeDatabaseService.GetActiveListingsAsync()
            : await firestoreService.GetActiveListingsAsync();
    }

    public async Task<int> GetActiveListingCountAsync()
    {
        if (!IsEnabled)
        {
            return 0;
        }

        return UseRealtimeDatabase
            ? await realtimeDatabaseService.GetActiveListingCountAsync()
            : await firestoreService.GetActiveListingCountAsync();
    }

    public async Task<QuartermasterListing?> GetListingAsync(string listingId)
    {
        if (!IsEnabled)
        {
            return null;
        }

        return UseRealtimeDatabase
            ? await realtimeDatabaseService.GetListingAsync(listingId)
            : await firestoreService.GetListingAsync(listingId);
    }

    public async Task<int> TryPurchaseListingQuantityAsync(string listingId, string buyerProfileId, int quantity, string idempotencyKey)
    {
        if (!IsEnabled)
        {
            return 0;
        }

        return UseRealtimeDatabase
            ? await realtimeDatabaseService.TryPurchaseListingQuantityAsync(listingId, buyerProfileId, quantity, idempotencyKey)
            : await firestoreService.TryPurchaseListingQuantityAsync(listingId, buyerProfileId, quantity, idempotencyKey);
    }

    public async Task CompleteListingPurchaseAsync(string listingId, string idempotencyKey)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (UseRealtimeDatabase)
        {
            await realtimeDatabaseService.CompleteListingPurchaseAsync(listingId, idempotencyKey);
        }
        else
        {
            await firestoreService.CompleteListingPurchaseAsync(listingId, idempotencyKey);
        }
    }

    public async Task ReleaseListingQuantityAsync(string listingId, string idempotencyKey)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (UseRealtimeDatabase)
        {
            await realtimeDatabaseService.ReleaseListingQuantityAsync(listingId, idempotencyKey);
        }
        else
        {
            await firestoreService.ReleaseListingQuantityAsync(listingId, idempotencyKey);
        }
    }

    public async Task CleanupExpiredListingsAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        if (UseRealtimeDatabase)
        {
            await realtimeDatabaseService.CleanupExpiredListingsAsync();
        }
        else
        {
            await firestoreService.CleanupExpiredListingsAsync();
        }
    }

    public async Task DeleteExpiredListingsAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        if (UseRealtimeDatabase)
        {
            await realtimeDatabaseService.DeleteExpiredListingsAsync();
        }
        else
        {
            await firestoreService.DeleteExpiredListingsAsync();
        }
    }

    public async Task RebuildCatalogueAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        if (UseRealtimeDatabase)
        {
            await realtimeDatabaseService.RebuildCatalogueAsync();
        }
    }
}
