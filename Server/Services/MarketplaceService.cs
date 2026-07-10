using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class MarketplaceService(
    RealtimeDatabaseService realtimeDatabaseService
)
{
    public bool IsEnabled => realtimeDatabaseService.IsEnabled;

    public async Task InitialiseAsync()
    {
        await realtimeDatabaseService.InitialiseAsync();
    }

    public Task<QuartermasterListing?> UploadListingAsync(QuartermasterListing listing)
    {
        if (!IsEnabled)
        {
            return Task.FromResult<QuartermasterListing?>(null);
        }

        return realtimeDatabaseService.UploadListingAsync(listing);
    }

    public Task<List<QuartermasterListing>> GetActiveListingsAsync()
    {
        if (!IsEnabled)
        {
            return Task.FromResult(new List<QuartermasterListing>());
        }

        return realtimeDatabaseService.GetActiveListingsAsync();
    }

    public Task<int> GetActiveListingCountAsync()
    {
        if (!IsEnabled)
        {
            return Task.FromResult(0);
        }

        return realtimeDatabaseService.GetActiveListingCountAsync();
    }

    public Task<QuartermasterListing?> GetListingAsync(string listingId)
    {
        if (!IsEnabled)
        {
            return Task.FromResult<QuartermasterListing?>(null);
        }

        return realtimeDatabaseService.GetListingAsync(listingId);
    }

    public Task<int> TryPurchaseListingQuantityAsync(string listingId, string buyerProfileId, int quantity, string idempotencyKey)
    {
        if (!IsEnabled)
        {
            return Task.FromResult(0);
        }

        return realtimeDatabaseService.TryPurchaseListingQuantityAsync(listingId, buyerProfileId, quantity, idempotencyKey);
    }

    public Task CompleteListingPurchaseAsync(string listingId, string idempotencyKey)
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        return realtimeDatabaseService.CompleteListingPurchaseAsync(listingId, idempotencyKey);
    }

    public Task ReleaseListingQuantityAsync(string listingId, string idempotencyKey)
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        return realtimeDatabaseService.ReleaseListingQuantityAsync(listingId, idempotencyKey);
    }

    public Task CleanupExpiredListingsAsync()
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        return realtimeDatabaseService.CleanupExpiredListingsAsync();
    }

    public Task DeleteExpiredListingsAsync()
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        return realtimeDatabaseService.DeleteExpiredListingsAsync();
    }

    public Task RebuildCatalogueAsync()
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        return realtimeDatabaseService.RebuildCatalogueAsync();
    }
}
