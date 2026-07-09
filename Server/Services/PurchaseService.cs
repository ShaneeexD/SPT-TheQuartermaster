using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using TheQuartermaster.Server.Models;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class PurchaseService(
    ISptLogger<PurchaseService> logger,
    ConfigService configService,
    TraderService traderService,
    FirestoreService firestoreService,
    ItemCompatibilityService itemCompatibilityService,
    ItemCloneService itemCloneService,
    ItemHelper itemHelper,
    PaymentService paymentService,
    InventoryHelper inventoryHelper,
    ProfileHelper profileHelper,
    TimeUtil timeUtil,
    HttpResponseUtil httpResponseUtil
)
{
    public async Task<bool> PurchaseItem(
        PmcData pmcData,
        ProcessBuyTradeRequestData buyRequestData,
        MongoId sessionID,
        bool foundInRaid,
        ItemEventRouterResponse output
    )
    {
        if (!configService.Config.ModEnabled)
        {
            return false;
        }

        if (buyRequestData.TransactionId != QuartermasterConstants.TraderId)
        {
            return false;
        }

        var itemId = buyRequestData.ItemId;
        var stackInfo = traderService.GetStackInfoForAssortItem(itemId);
        if (stackInfo is null || stackInfo.Allocations.Count == 0)
        {
            httpResponseUtil.AppendErrorToOutput(
                output,
                "[TheQuartermaster] Listing not found for selected item.",
                BackendErrorCodes.OfferNotFound
            );
            return false;
        }

        var count = buyRequestData.Count.GetValueOrDefault();
        if (count <= 0)
        {
            count = 1;
        }

        try
        {
            var firstAllocation = stackInfo.Allocations[0];
            var representativeListing = await firestoreService.GetListingAsync(firstAllocation.ListingId);
            if (representativeListing is null || representativeListing.Status != ListingStatus.Active || representativeListing.ExpiresAt?.ToDateTime() < DateTime.UtcNow)
            {
                httpResponseUtil.AppendErrorToOutput(
                    output,
                    "[TheQuartermaster] Offer no longer available or already sold.",
                    BackendErrorCodes.OfferOutOfStock
                );
                return false;
            }

            if (!itemCompatibilityService.IsListingCompatibleForBuyer(representativeListing, pmcData))
            {
                httpResponseUtil.AppendErrorToOutput(
                    output,
                    "[TheQuartermaster] Your profile cannot receive this item.",
                    BackendErrorCodes.UnknownTradingError
                );
                return false;
            }

            var itemTree = itemCloneService.DeserializeItemTree(representativeListing.ItemTreeJson);
            if (itemTree is null || itemTree.Count == 0)
            {
                httpResponseUtil.AppendErrorToOutput(
                    output,
                    "[TheQuartermaster] Failed to load item data for purchase.",
                    BackendErrorCodes.UnknownTradingError
                );
                return false;
            }

            var root = itemTree[0];
            var isStackable = IsItemStackable(root.Template.ToString());
            if (!isStackable && count > 1)
            {
                httpResponseUtil.AppendErrorToOutput(
                    output,
                    "[TheQuartermaster] Cannot purchase multiple non-stackable items at once.",
                    BackendErrorCodes.UnknownTradingError
                );
                return false;
            }

            var totalAvailable = stackInfo.Allocations.Sum(a => a.Quantity);
            if (count > totalAvailable)
            {
                httpResponseUtil.AppendErrorToOutput(
                    output,
                    "[TheQuartermaster] Not enough stock available.",
                    BackendErrorCodes.OfferOutOfStock
                );
                return false;
            }

            // Pay for the requested quantity (client has already computed the total price)
            paymentService.PayMoney(pmcData, buyRequestData, sessionID, output);
            if (output.Warnings?.Count > 0)
            {
                return false;
            }

            // Reserve/consume the stock from the underlying listings
            var remainingToConsume = count;
            foreach (var allocation in stackInfo.Allocations)
            {
                if (remainingToConsume <= 0)
                {
                    break;
                }

                var consumed = await firestoreService.TryPurchaseListingQuantityAsync(allocation.ListingId, sessionID.ToString(), remainingToConsume);
                if (consumed <= 0)
                {
                    break;
                }

                remainingToConsume -= consumed;
            }

            if (remainingToConsume > 0)
            {
                logger.Warning($"[TheQuartermaster] Player {sessionID} paid for {count} items but only {count - remainingToConsume} were available from stock.");
                httpResponseUtil.AppendErrorToOutput(
                    output,
                    "[TheQuartermaster] Stock changed during purchase, please try again.",
                    BackendErrorCodes.OfferOutOfStock
                );
                return false;
            }

            // Build the item(s) to deliver
            var deliveredTree = isStackable
                ? BuildStackableDeliveryTree(itemTree, count)
                : itemCloneService.CloneAndRemap(itemTree);

            var addRequest = new AddItemsDirectRequest
            {
                ItemsWithModsToAdd = [deliveredTree],
                FoundInRaid = foundInRaid,
                Callback = null,
                UseSortingTable = false
            };
            inventoryHelper.AddItemsToStash(sessionID, addRequest, pmcData, output);
            if (output.Warnings?.Count > 0)
            {
                return false;
            }

            // Track this purchase against the player's per-restock limit
            TrackPurchase(sessionID, QuartermasterConstants.TraderId, itemId, count);

            logger.Info($"[TheQuartermaster] Player {sessionID} purchased {count} of {representativeListing.RootTpl}.");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Purchase failed: {ex.Message}", ex);
            httpResponseUtil.AppendErrorToOutput(
                output,
                "[TheQuartermaster] Purchase failed.",
                BackendErrorCodes.UnknownTradingError
            );
            return false;
        }
    }

    private bool IsItemStackable(string? tpl)
    {
        if (string.IsNullOrWhiteSpace(tpl))
        {
            return false;
        }

        var template = itemHelper.GetItem(new MongoId(tpl)).Value;
        return template?.Properties?.StackMaxSize > 1;
    }

    private List<Item> BuildStackableDeliveryTree(List<Item> sourceTree, int count)
    {
        var deliveredTree = itemCloneService.CloneAndRemap(sourceTree);
        var root = deliveredTree[0];
        root.Upd ??= new Upd();
        root.Upd.StackObjectsCount = count;
        return deliveredTree;
    }

    private void TrackPurchase(MongoId sessionID, MongoId traderId, MongoId itemId, int count)
    {
        var profile = profileHelper.GetFullProfile(sessionID);
        if (profile is null)
        {
            return;
        }

        profile.TraderPurchases ??= new Dictionary<MongoId, Dictionary<MongoId, TraderPurchaseData>?>();
        if (!profile.TraderPurchases.TryGetValue(traderId, out var traderPurchases) || traderPurchases is null)
        {
            traderPurchases = new Dictionary<MongoId, TraderPurchaseData>();
            profile.TraderPurchases[traderId] = traderPurchases;
        }

        if (!traderPurchases.TryGetValue(itemId, out var data) || data is null)
        {
            data = new TraderPurchaseData();
            traderPurchases[itemId] = data;
        }

        data.PurchaseCount = (data.PurchaseCount ?? 0) + count;
        data.PurchaseTimestamp = timeUtil.GetTimeStamp();
    }

}
