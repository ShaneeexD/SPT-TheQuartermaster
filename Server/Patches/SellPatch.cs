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
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using TheQuartermaster.Server.Services;
using TheQuartermaster.Server.Services.Contracts;
using TqmServices = TheQuartermaster.Server.Services;

namespace TheQuartermaster.Server.Patches;

[Injectable(InjectionType.Singleton)]
public class SellPatch : AbstractPatch
{
    private static ConfigService? _configService;
    private static BackendConfigService? _backendConfigService;
    private static ListingService? _listingService;
    private static ItemCloneService? _itemCloneService;
    private static MarketplaceService? _marketplaceService;
    private static InventoryHelper? _inventoryHelper;
    private static PaymentService? _paymentService;
    private static QuestHelper? _questHelper;
    private static TqmServices.TraderService? _traderService;
    private static ItemOverrideService? _itemOverrideService;
    private static ItemHelper? _itemHelper;
    private static ISptLogger<SellPatch>? _logger;
    private static HttpResponseUtil? _httpResponseUtil;

    public static void SetDependencies(
        ConfigService configService,
        BackendConfigService backendConfigService,
        ListingService listingService,
        ItemCloneService itemCloneService,
        MarketplaceService marketplaceService,
        InventoryHelper inventoryHelper,
        PaymentService paymentService,
        QuestHelper questHelper,
        TqmServices.TraderService traderService,
        ItemOverrideService itemOverrideService,
        ItemHelper itemHelper,
        ISptLogger<SellPatch> logger,
        HttpResponseUtil httpResponseUtil
    )
    {
        _configService = configService;
        _backendConfigService = backendConfigService;
        _listingService = listingService;
        _itemCloneService = itemCloneService;
        _marketplaceService = marketplaceService;
        _inventoryHelper = inventoryHelper;
        _paymentService = paymentService;
        _questHelper = questHelper;
        _traderService = traderService;
        _itemOverrideService = itemOverrideService;
        _itemHelper = itemHelper;
        _logger = logger;
        _httpResponseUtil = httpResponseUtil;
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

        try
        {
            if (_marketplaceService?.IsEnabled == true && _backendConfigService is not null)
            {
                var activeCount = _marketplaceService.GetActiveListingCountAsync().GetAwaiter().GetResult();
                var cap = _backendConfigService.Config.MaxActiveListings;
                if (activeCount >= cap)
                {
                    _logger?.DebugWarning($"[TheQuartermaster] Global active listing cap reached ({activeCount}/{cap}); blocking sale.");
                    _httpResponseUtil?.AppendErrorToOutput(
                        output,
                        "[TheQuartermaster] The global marketplace is full. Try again later.",
                        BackendErrorCodes.OfferOutOfStock
                    );
                    return false;
                }
            }

            _questHelper?.IncrementSoldToTraderCounters(profileWithItemsToSell, profileToReceiveMoney, sellRequest);

            const string pattern = @"\s+";
            var uploaded = 0;
            var totalComputedPrice = 0L;
            var buyMultiplier = _traderService?.GetBuyPriceMultiplier() ?? 0.6;

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

                var rootItem = itemTree[0];
                var rootTemplate = _itemHelper?.GetItem(rootItem.Template).Value;
                if (_traderService is not null && !_traderService.CanBuyItem(rootItem.Template, rootTemplate?.Parent))
                {
                    _logger?.DebugWarning($"[TheQuartermaster] Skipping sell of {rootItem.Template}: not in the Quartermaster's buy filters.");
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
                    _logger?.DebugWarning($"[TheQuartermaster] Could not create listing for item {itemIdToFind}.");
                    _httpResponseUtil?.AppendErrorToOutput(
                        output,
                        $"[TheQuartermaster] Could not list item {itemIdToFind}: it may exceed the max allowed stack size.",
                        BackendErrorCodes.OfferOutOfStock
                    );
                    continue;
                }

                foreach (var item in itemTree)
                {
                    var quantity = (int)(item.Upd?.StackObjectsCount ?? 1);
                    var tpl = item.Template.ToString();
                    if (_itemOverrideService?.TryGetPrice(tpl, out var overridePrice, out _) == true)
                    {
                        totalComputedPrice += overridePrice * quantity;
                    }
                    else if (_itemHelper is not null)
                    {
                        totalComputedPrice += (long)(_itemHelper.GetStaticItemPrice(item.Template) * buyMultiplier * quantity);
                    }
                }

                if (_configService?.Config.UploadConsent == true)
                {
                    if (_marketplaceService?.IsEnabled != true)
                    {
                        _logger?.DebugWarning("[TheQuartermaster] Marketplace backend is not enabled, cannot upload listing.");
                        continue;
                    }

                    var uploadedListing = _marketplaceService.UploadListingAsync(listing).GetAwaiter().GetResult();
                    if (uploadedListing is null)
                    {
                        _logger?.Error($"[TheQuartermaster] Failed to upload listing for item {itemIdToFind}.");
                        continue;
                    }

                    _logger?.DebugInfo($"[TheQuartermaster] Listed item {itemIdToFind} as listing {uploadedListing.Id}.");
                }
                else
                {
                    _logger?.DebugInfo($"[TheQuartermaster] Upload consent disabled; selling {itemIdToFind} locally without global listing.");
                }

                _inventoryHelper?.RemoveItem(profileWithItemsToSell, itemId, sessionID, output);
                uploaded++;
            }

            _logger?.DebugInfo($"[TheQuartermaster] Uploaded {uploaded} listing(s) from player {sessionID}.");

            if (totalComputedPrice > 0 && sellRequest.Price != (int)totalComputedPrice)
            {
                _logger?.DebugInfo($"[TheQuartermaster] Overriding sell-to-trader price from {sellRequest.Price} to {totalComputedPrice}.");
                sellRequest.Price = (int)totalComputedPrice;
            }

            _paymentService?.GiveProfileMoney(profileToReceiveMoney, sellRequest.Price, sellRequest, output, sessionID);

            return false; // Skip original SellItem
        }
        catch (Exception ex)
        {
            _logger?.Error($"[TheQuartermaster] SellPatch error: {ex.Message}", ex);
            return true;
        }
    }
}
