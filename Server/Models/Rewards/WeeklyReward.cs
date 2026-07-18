using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Rewards;

public class WeeklyReward
{
    [JsonPropertyName("week")]
    public string Week { get; set; } = string.Empty;

    [JsonPropertyName("rewardId")]
    public string RewardId { get; set; } = string.Empty;

    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("generatedAt")]
    public long GeneratedAt { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("contents")]
    public List<RewardPackageContent> Contents { get; set; } = [];
}
