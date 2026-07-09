using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Services;
using TqmServices = TheQuartermaster.Server.Services;

namespace TheQuartermaster.Server.Patches;

[Injectable(InjectionType.Singleton)]
public class SellPatch : AbstractPatch
{
    private static ConfigService? _configService;
    private static ListingService? _listingService;
    private static ItemCloneService? _itemCloneService;
    private static FirestoreService? _firestoreService;
    private static InventoryHelper? _inventoryHelper;
    private static PaymentService? _paymentService;
    private static QuestHelper? _questHelper;
    private static TqmServices.TraderService? _traderService;
    private static ISptLogger<SellPatch>? _logger;

    public static void SetDependencies(
        ConfigService configService,
        ListingService listingService,
        ItemCloneService itemCloneService,
        FirestoreService firestoreService,
        InventoryHelper inventoryHelper,
        PaymentService paymentService,
        QuestHelper questHelper,
        TqmServices.TraderService traderService,
        ISptLogger<SellPatch> logger
    )
    {
        _configService = configService;
        _listingService = listingService;
        _itemCloneService = itemCloneService;
        _firestoreService = firestoreService;
        _inventoryHelper = inventoryHelper;
        _paymentService = paymentService;
        _questHelper = questHelper;
        _traderService = traderService;
        _logger = logger;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TradeHelper), nameof(TradeHelper.SellItem));
    }

    [PatchPrefix]
    private static bool Prefix(
        PmcData profileWithItemsToSell,
        PmcData profileToReceiveMoney,
        ProcessSellTradeRequestData sellRequest,
        MongoId sessionID,
        ItemEventRouterResponse output
    )
    {
        if (sellRequest.TransactionId != QuartermasterConstants.TraderId)
        {
            return true; // Not our trader, run original
        }

        if (_configService?.Config.ModEnabled != true)
        {
            return true;
        }

        _questHelper?.IncrementSoldToTraderCounters(profileWithItemsToSell, profileToReceiveMoney, sellRequest);

        try
        {
            const string pattern = @"\s+";
            var uploaded = 0;

            foreach (var itemToBeRemoved in sellRequest.Items ?? new List<SoldItem>())
            {
                var itemIdToFind = Regex.Replace(itemToBeRemoved.Id.ToString(), pattern, "");
                if (!MongoId.IsValidMongoId(itemIdToFind))
                {
                    continue;
                }

                var itemId = new MongoId(itemIdToFind);

                var itemTree = profileWithItemsToSell.Inventory?.Items?.GetItemWithChildren(itemId);
                if (itemTree is null || !itemTree.Any())
                {
                    continue;
                }

                var serialized = _itemCloneService?.SerializeItemTree(itemTree);
                if (string.IsNullOrWhiteSpace(serialized))
                {
                    continue;
                }

                var deserialized = _itemCloneService?.DeserializeItemTree(serialized);
                if (deserialized is null || !deserialized.Any())
                {
                    continue;
                }

                var listing = _listingService?.CreateListing(
                    deserialized,
                    sessionID.ToString(),
                    sessionID.ToString()
                );

                if (listing is null)
                {
                    _logger?.Warning($"[TheQuartermaster] Could not create listing for item {itemIdToFind}.");
                    continue;
                }

                if (_configService?.Config.UploadConsent == true)
                {
                    if (_firestoreService?.IsEnabled != true)
                    {
                        _logger?.Warning("[TheQuartermaster] Firestore is not enabled, cannot upload listing.");
                        continue;
                    }

                    var uploadedListing = _firestoreService.UploadListingAsync(listing).GetAwaiter().GetResult();
                    if (uploadedListing is null)
                    {
                        _logger?.Error($"[TheQuartermaster] Failed to upload listing for item {itemIdToFind}.");
                        continue;
                    }

                    _logger?.Info($"[TheQuartermaster] Listed item {itemIdToFind} as listing {uploadedListing.Id}.");
                }
                else
                {
                    _logger?.Info($"[TheQuartermaster] Upload consent disabled; selling {itemIdToFind} locally without global listing.");
                }

                _inventoryHelper?.RemoveItem(profileWithItemsToSell, itemId, sessionID, output);
                uploaded++;
            }

            _logger?.Info($"[TheQuartermaster] Uploaded {uploaded} listing(s) from player {sessionID}.");

            _paymentService?.GiveProfileMoney(profileToReceiveMoney, sellRequest.Price, sellRequest, output, sessionID);

            if (uploaded > 0 && _configService?.Config.UploadConsent == true)
            {
                _logger?.Info("[TheQuartermaster] Refreshing trader assort after sale.");
                _traderService?.RefreshAssort().GetAwaiter().GetResult();
            }

            return false; // Skip original SellItem
        }
        catch (Exception ex)
        {
            _logger?.Error($"[TheQuartermaster] SellPatch error: {ex.Message}", ex);
            return true;
        }
    }
}
