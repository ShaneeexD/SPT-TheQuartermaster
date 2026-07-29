using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Rewards;

public class RewardDataBundle
{
    [JsonPropertyName("version")]
    public long Version { get; set; }

    [JsonPropertyName("generated_at")]
    public long GeneratedAt { get; set; }

    [JsonPropertyName("weeklyReward")]
    public WeeklyReward WeeklyReward { get; set; } = new();

    [JsonPropertyName("communityStats")]
    public CommunityStats CommunityStats { get; set; } = new();

    [JsonPropertyName("playerCount")]
    public PlayerCountStats PlayerCount { get; set; } = new();

    [JsonPropertyName("jackpotWinner")]
    public JackpotWinner? JackpotWinner { get; set; }
}
