using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Helpers;
using TheQuartermaster.Server.Services;

namespace TheQuartermaster.Server.Patches;

[Injectable(InjectionType.Singleton)]
public class BuyPatch : AbstractPatch
{
    private static PurchaseService? _purchaseService;
    private static ISptLogger<BuyPatch>? _logger;

    public static void SetDependencies(PurchaseService purchaseService, ISptLogger<BuyPatch> logger)
    {
        _purchaseService = purchaseService;
        _logger = logger;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TradeHelper), nameof(TradeHelper.BuyItem));
    }

    [PatchPrefix]
    private static bool Prefix(
        PmcData pmcData,
        ProcessBuyTradeRequestData buyRequestData,
        MongoId sessionID,
        bool foundInRaid,
        ItemEventRouterResponse output
    )
    {
        if (buyRequestData.TransactionId != QuartermasterConstants.TraderId)
        {
            return true;
        }

        if (_purchaseService is null)
        {
            _logger?.Error("[TheQuartermaster] PurchaseService not available, skipping patch.");
            return true;
        }

        try
        {
            var result = _purchaseService.PurchaseItem(pmcData, buyRequestData, sessionID, foundInRaid, output)
                .GetAwaiter()
                .GetResult();

            if (result)
            {
                _logger?.DebugInfo($"[TheQuartermaster] Intercepted purchase for player {sessionID}, listing {buyRequestData.ItemId}.");
                return false; // Skip original
            }

            return false; // Don't run original if we attempted purchase to avoid double processing
        }
        catch (Exception ex)
        {
            _logger?.Error($"[TheQuartermaster] BuyPatch error: {ex.Message}", ex);
            return false;
        }
    }
}
