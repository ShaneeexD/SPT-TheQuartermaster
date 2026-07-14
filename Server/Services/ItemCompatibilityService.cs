using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Helpers;
using TheQuartermaster.Server.Models;
using TheQuartermaster.Server.Services.Contracts;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class ItemCompatibilityService(
    ISptLogger<ItemCompatibilityService> logger,
    BackendConfigService backendConfigService,
    VanillaAllowlistService vanillaAllowlistService,
    ItemHelper itemHelper,
    MarketplaceService marketplaceService
)
{
    public bool IsListingValidForUpload(List<Item> itemTree)
    {
        if (itemTree.Count == 0)
        {
            return false;
        }

        var root = itemTree.First();
        if (root.Upd?.StackObjectsCount > 1 && itemTree.Count > 1)
        {
            logger.DebugWarning("[TheQuartermaster] Cannot upload stacked item with children.");
            return false;
        }

        var templates = itemTree.Select(i => i.Template).ToList();

        if (backendConfigService.Config.VanillaItemsOnly && !vanillaAllowlistService.AllVanilla(templates))
        {
            logger.DebugWarning("[TheQuartermaster] Vanilla-only mode is enabled and listing contains non-vanilla templates.");
            return false;
        }

        if (templates.Any(t => QuartermasterConstants.ExcludedTpls.Contains(t.ToString())))
        {
            logger.DebugWarning("[TheQuartermaster] Listing contains an excluded template.");
            return false;
        }

        if (itemTree.Count > QuartermasterConstants.Marketplace.MaxItemTreeSize)
        {
            logger.DebugWarning($"[TheQuartermaster] Listing tree size {itemTree.Count} exceeds max {QuartermasterConstants.Marketplace.MaxItemTreeSize}.");
            return false;
        }

        if (!itemHelper.IsValidItem(root.Template))
        {
            logger.DebugWarning($"[TheQuartermaster] Root item {root.Template} is not a valid item.");
            return false;
        }

        var rootTemplate = itemHelper.GetItem(root.Template).Value;
        var rootParentId = rootTemplate?.Parent.ToString() ?? string.Empty;
        if (root.Upd?.StackObjectsCount > 1 && !QuartermasterConstants.Marketplace.StackableParentIds.Contains(rootParentId))
        {
            logger.DebugWarning($"[TheQuartermaster] Root item {root.Template} is a stack but its parent category is not allowed for stacking.");
            return false;
        }

        if (!QuartermasterConstants.Marketplace.AmmoParentIds.Contains(rootParentId))
        {
            var limits = marketplaceService.GetListingLimits();
            var max = limits.MaxQuantityOverrides.TryGetValue(root.Template.ToString(), out var overrideMax) ? overrideMax : limits.DefaultMaxQuantity;
            if (max > 0)
            {
                var activeListings = marketplaceService.GetActiveListings();
                var rootTpl = root.Template.ToString();
                var existingQuantity = activeListings
                    .Where(l => string.Equals(l.RootTpl, rootTpl, StringComparison.OrdinalIgnoreCase))
                    .Sum(l => RealtimeDatabaseService.GetListingQuantity(l.ItemTreeJson));
                var newQuantity = root.Upd?.StackObjectsCount ?? 1;
                if (existingQuantity + newQuantity > max)
                {
                    logger.DebugWarning($"[TheQuartermaster] Listing {rootTpl} would exceed max quantity (existing {existingQuantity}, new {newQuantity}, max {max}).");
                    return false;
                }
            }
        }

        return true;
    }

    public bool IsListingCompatibleForBuyer(QuartermasterListing listing, PmcData buyer)
    {
        if (listing.Status != ListingStatus.Active)
        {
            return false;
        }

        if (listing.ExpiresAt is not null && listing.ExpiresAt.Value.ToDateTime() < DateTime.UtcNow)
        {
            return false;
        }

        if (backendConfigService.Config.VanillaItemsOnly && !listing.IsVanilla)
        {
            return false;
        }

        foreach (var tpl in listing.RequiredTpls)
        {
            if (!vanillaAllowlistService.IsTemplateInRuntimeDatabase(tpl))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsVanilla(List<Item> itemTree)
    {
        var templates = itemTree.Select(i => i.Template).ToList();
        return vanillaAllowlistService.AllVanilla(templates);
    }

    public List<MongoId> GetRequiredTemplates(List<Item> itemTree)
    {
        return itemTree.Select(i => i.Template).Distinct().ToList();
    }
}
