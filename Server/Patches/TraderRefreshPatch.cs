using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using TheQuartermaster.Server.Services;
using TheQuartermaster.Server.Services.Contracts;

namespace TheQuartermaster.Server.Patches;

[Injectable]
public class TraderRefreshPatch : AbstractPatch
{
    private static TraderService? _traderService;
    private static IServiceProvider? _serviceProvider;

    public TraderRefreshPatch(TraderService traderService, IServiceProvider serviceProvider) : base("TheQuartermaster.TraderRefreshPatch")
    {
        _traderService = traderService;
        _serviceProvider = serviceProvider;
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

        var communityContractService = _serviceProvider?.GetService(typeof(CommunityContractService)) as CommunityContractService;
        communityContractService?.RefreshAsync().GetAwaiter().GetResult();
    }
}
