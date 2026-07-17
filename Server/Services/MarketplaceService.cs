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

    public void AddListingToCache(QuartermasterListing listing)
    {
        if (!IsEnabled)
        {
            return;
        }

        realtimeDatabaseService.AddListingToCache(listing);
    }

    public void RemoveListingFromCache(string listingId)
    {
        if (!IsEnabled)
        {
            return;
        }

        realtimeDatabaseService.RemoveListingFromCache(listingId);
    }

    public Task<List<QuartermasterListing>> GetActiveListingsAsync()
    {
        if (!IsEnabled)
        {
            return Task.FromResult(new List<QuartermasterListing>());
        }

        return realtimeDatabaseService.GetActiveListingsAsync();
    }

    public List<QuartermasterListing> GetActiveListings()
    {
        if (!IsEnabled)
        {
            return new List<QuartermasterListing>();
        }

        if (realtimeDatabaseService.IsCacheInitialized)
        {
            return realtimeDatabaseService.GetCachedActiveListings();
        }

        return realtimeDatabaseService.GetActiveListingsAsync().GetAwaiter().GetResult();
    }

    public Task<int> GetActiveListingCountAsync()
    {
        if (!IsEnabled)
        {
            return Task.FromResult(0);
        }

        return realtimeDatabaseService.GetActiveListingCountAsync();
    }

    public Task<int> GetActiveListingCountExcludingScavengedAsync()
    {
        if (!IsEnabled)
        {
            return Task.FromResult(0);
        }

        return realtimeDatabaseService.GetActiveListingCountExcludingScavengedAsync();
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

    public Task CleanupSoldListingsAsync()
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        return realtimeDatabaseService.CleanupSoldListingsAsync();
    }

    public Task RebuildCatalogueAsync()
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        return realtimeDatabaseService.RebuildCatalogueAsync();
    }

    public Task<RtdbBuyFilters> GetBuyFiltersAsync()
    {
        return realtimeDatabaseService.GetBuyFiltersAsync();
    }

    public Task SaveBuyFiltersAsync(RtdbBuyFilters filters)
    {
        return realtimeDatabaseService.SaveBuyFiltersAsync(filters);
    }

    public RtdbListingLimits GetListingLimits()
    {
        return realtimeDatabaseService.GetListingLimits();
    }

    public Task<RtdbListingLimits> GetListingLimitsAsync()
    {
        return realtimeDatabaseService.GetListingLimitsAsync();
    }

    public Task SaveListingLimitsAsync(RtdbListingLimits limits)
    {
        return realtimeDatabaseService.SaveListingLimitsAsync(limits);
    }
}
