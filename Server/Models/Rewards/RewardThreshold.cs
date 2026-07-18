using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Rewards;

public class RewardThreshold
{
    [JsonPropertyName("minSpend")]
    public long MinSpend { get; set; }

    [JsonPropertyName("rewardPool")]
    public string RewardPool { get; set; } = string.Empty;
}
