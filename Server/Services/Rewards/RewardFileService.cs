using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models.Rewards;
using TheQuartermaster.Server.Services.Contracts;

namespace TheQuartermaster.Server.Services.Rewards;

/// <summary>
/// Mirrors ContractFileService for the reward portion of the VM-cached data.json bundle.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class RewardFileService(
    ISptLogger<RewardFileService> logger,
    ContractFileService contractFileService
)
{
    public async Task<RewardDataBundle?> GetRewardDataBundleAsync()
    {
        var bundle = await contractFileService.TryGetBundleAsync();
        if (bundle is null)
        {
            logger.DebugDebug("[TheQuartermaster] Reward data bundle unavailable.");
            return null;
        }

        return new RewardDataBundle
        {
            Version = bundle.Version,
            GeneratedAt = bundle.GeneratedAt.HasValue
                ? ((DateTimeOffset) bundle.GeneratedAt.Value.ToDateTime()).ToUnixTimeSeconds()
                : 0,
            WeeklyReward = bundle.WeeklyReward,
            CommunityStats = bundle.CommunityStats,
            PlayerCount = bundle.PlayerCount,
            JackpotWinner = bundle.JackpotWinner
        };
    }

    public async Task<WeeklyReward?> GetWeeklyRewardAsync()
    {
        var bundle = await GetRewardDataBundleAsync();
        return bundle?.WeeklyReward;
    }

    public async Task<CommunityStats?> GetCommunityStatsAsync()
    {
        var bundle = await GetRewardDataBundleAsync();
        return bundle?.CommunityStats;
    }

    public async Task<PlayerCountStats?> GetPlayerCountStatsAsync()
    {
        var bundle = await GetRewardDataBundleAsync();
        return bundle?.PlayerCount;
    }

    public async Task<JackpotWinner?> GetJackpotWinnerAsync()
    {
        var bundle = await GetRewardDataBundleAsync();
        return bundle?.JackpotWinner;
    }
}
