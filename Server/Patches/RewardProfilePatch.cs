using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Services.Rewards;

namespace TheQuartermaster.Server.Patches;

[Injectable(InjectionType.Singleton)]
public class RewardProfilePatch : AbstractPatch
{
    private static CommunityRewardService? _communityRewardService;
    private static ISptLogger<RewardProfilePatch>? _logger;
    [ThreadStatic]
    private static bool _isProcessing;

    public static void SetDependencies(CommunityRewardService communityRewardService, ISptLogger<RewardProfilePatch> logger)
    {
        _communityRewardService = communityRewardService;
        _logger = logger;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ProfileHelper), nameof(ProfileHelper.GetFullProfile), [typeof(MongoId)]);
    }

    [PatchPostfix]
    private static void Postfix(MongoId sessionId, SptProfile? __result)
    {
        if (_communityRewardService is null || _isProcessing)
        {
            return;
        }

        try
        {
            _isProcessing = true;
            _communityRewardService.TryClaimWeeklyReward(sessionId, __result, __result?.CharacterData?.PmcData)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _logger?.Error($"[TheQuartermaster] RewardProfilePatch error: {ex.Message}", ex);
        }
        finally
        {
            _isProcessing = false;
        }
    }
}
