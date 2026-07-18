using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Rewards;

public class PlayerRewardClaim
{
    [JsonPropertyName("week")]
    public string Week { get; set; } = string.Empty;

    [JsonPropertyName("rewardId")]
    public string RewardId { get; set; } = string.Empty;

    [JsonPropertyName("claimedAt")]
    public long ClaimedAt { get; set; }
}
