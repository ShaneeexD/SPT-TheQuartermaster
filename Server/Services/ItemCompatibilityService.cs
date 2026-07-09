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
    ItemHelper itemHelper
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
            logger.Warning("[TheQuartermaster] Cannot upload stacked item with children.");
            return false;
        }

        var templates = itemTree.Select(i => i.Template).ToList();

        if (backendConfigService.Config.VanillaItemsOnly && !vanillaAllowlistService.AllVanilla(templates))
        {
            logger.Warning("[TheQuartermaster] Vanilla-only mode is enabled and listing contains non-vanilla templates.");
            return false;
        }

        if (templates.Any(t => QuartermasterConstants.ExcludedTpls.Contains(t.ToString())))
        {
            logger.Warning("[TheQuartermaster] Listing contains an excluded template.");
            return false;
        }

        if (itemTree.Count > QuartermasterConstants.Marketplace.MaxItemTreeSize)
        {
            logger.Warning($"[TheQuartermaster] Listing tree size {itemTree.Count} exceeds max {QuartermasterConstants.Marketplace.MaxItemTreeSize}.");
            return false;
        }

        if (!itemHelper.IsValidItem(root.Template))
        {
            logger.Warning($"[TheQuartermaster] Root item {root.Template} is not a valid item.");
            return false;
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
