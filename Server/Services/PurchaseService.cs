using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
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
    PaymentService paymentService,
    InventoryHelper inventoryHelper,
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

        var listingId = traderService.GetListingIdForAssortItem(buyRequestData.ItemId);
        if (string.IsNullOrWhiteSpace(listingId))
        {
            httpResponseUtil.AppendErrorToOutput(
                output,
                "[TheQuartermaster] Listing not found for selected item.",
                BackendErrorCodes.OfferNotFound
            );
            return false;
        }

        try
        {
            var listing = await firestoreService.GetListingAsync(listingId);
            if (listing is null || listing.Status != ListingStatus.Active || listing.ExpiresAt?.ToDateTime() < DateTime.UtcNow)
            {
                httpResponseUtil.AppendErrorToOutput(
                    output,
                    "[TheQuartermaster] Offer no longer available or already sold.",
                    BackendErrorCodes.OfferOutOfStock
                );
                return false;
            }

            if (!itemCompatibilityService.IsListingCompatibleForBuyer(listing, pmcData))
            {
                httpResponseUtil.AppendErrorToOutput(
                    output,
                    "[TheQuartermaster] Your profile cannot receive this item.",
                    BackendErrorCodes.UnknownTradingError
                );
                return false;
            }

            var itemTree = itemCloneService.DeserializeItemTree(listing.ItemTreeJson);
            if (itemTree is null || itemTree.Count == 0)
            {
                httpResponseUtil.AppendErrorToOutput(
                    output,
                    "[TheQuartermaster] Failed to load item data for purchase.",
                    BackendErrorCodes.UnknownTradingError
                );
                return false;
            }

            var clonedTree = itemCloneService.CloneAndRemap(itemTree);

            // Pay for the item
            paymentService.PayMoney(pmcData, buyRequestData, sessionID, output);
            if (output.Warnings?.Count > 0)
            {
                return false;
            }

            // Deliver item to buyer
            var addRequest = new AddItemsDirectRequest
            {
                ItemsWithModsToAdd = [clonedTree],
                FoundInRaid = foundInRaid,
                Callback = null,
                UseSortingTable = false
            };
            inventoryHelper.AddItemsToStash(sessionID, addRequest, pmcData, output);
            if (output.Warnings?.Count > 0)
            {
                return false;
            }

            // Only mark as sold after successful payment and delivery
            var soldListing = await firestoreService.TryPurchaseListingAsync(listingId, sessionID.ToString());
            if (soldListing is null)
            {
                logger.Warning($"[TheQuartermaster] Delivered listing {listingId} but failed to mark it as sold in Firestore.");
            }

            logger.Info($"[TheQuartermaster] Player {sessionID} purchased listing {listing.Id} for {listing.MarketPrice} RUB.");
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

}
