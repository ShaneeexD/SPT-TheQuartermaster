using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using TheQuartermaster.Server.Services;
using TheQuartermaster.Server.Services.Contracts;

namespace TheQuartermaster.Server.Patches;

[Injectable]
public class TraderRefreshPatch : AbstractPatch
{
    private static TraderService? _traderService;
    private static CommunityContractService? _communityContractService;

    public TraderRefreshPatch()
        : base("TheQuartermaster.TraderRefreshPatch")
    {
    }

    public static void SetDependencies(TraderService traderService, CommunityContractService communityContractService)
    {
        _traderService = traderService;
        _communityContractService = communityContractService;
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

        _traderService?.RefreshAssort(sessionId).GetAwaiter().GetResult();
        _communityContractService?.RefreshAsync().GetAwaiter().GetResult();
    }
}
