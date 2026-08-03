using System.Reflection;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using TheQuartermaster.Server.Services.Rewards;

namespace TheQuartermaster.Server.Patches;

[Injectable(InjectionType.Singleton)]
public class RewardProfilePatch : AbstractPatch
{
    private static CommunityRewardService? _communityRewardService;
    private static ISptLogger<RewardProfilePatch>? _logger;
    private static readonly HashSet<string> _processingSessions = new();
    private static readonly object _lock = new();

    public RewardProfilePatch(CommunityRewardService communityRewardService, ISptLogger<RewardProfilePatch> logger) : base("TheQuartermaster.RewardProfilePatch")
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
        if (_communityRewardService is null)
        {
            return;
        }

        var sessionKey = sessionId.ToString();
        lock (_lock)
        {
            if (!_processingSessions.Add(sessionKey))
            {
                return;
            }
        }

        try
        {
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
            lock (_lock)
            {
                _processingSessions.Remove(sessionKey);
            }
        }
    }
}
