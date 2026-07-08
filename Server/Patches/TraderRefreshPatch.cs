using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using TheQuartermaster.Server.Services;

namespace TheQuartermaster.Server.Patches;

[Injectable]
public class TraderRefreshPatch : AbstractPatch
{
    private static TraderService? _traderService;

    public TraderRefreshPatch()
        : base("TheQuartermaster.TraderRefreshPatch")
    {
    }

    public static void SetDependencies(TraderService traderService)
    {
        _traderService = traderService;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TraderAssortHelper), nameof(TraderAssortHelper.GetAssort));
    }

    [PatchPrefix]
    private static void Prefix(MongoId sessionId, MongoId traderId, bool showLockedAssorts = false)
    {
        if (traderId != QuartermasterConstants.TraderId)
        {
            return;
        }

        _traderService?.RefreshAssort().GetAwaiter().GetResult();
    }
}
