using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Rewards;

public class CurrentWeekStats
{
    [JsonPropertyName("week")]
    public string Week { get; set; } = string.Empty;

    [JsonPropertyName("totalSpent")]
    public long TotalSpent { get; set; }
}

public class LifetimeStats
{
    [JsonPropertyName("totalSpent")]
    public long TotalSpent { get; set; }
}

public class CommunityStats
{
    [JsonPropertyName("currentWeek")]
    public CurrentWeekStats CurrentWeek { get; set; } = new();

    [JsonPropertyName("lifetime")]
    public LifetimeStats Lifetime { get; set; } = new();
}
