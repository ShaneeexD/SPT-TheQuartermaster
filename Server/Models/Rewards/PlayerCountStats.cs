using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Rewards;

public class PlayerCountStats
{
    [JsonPropertyName("day")]
    public string Day { get; set; } = string.Empty;

    [JsonPropertyName("current")]
    public int Current { get; set; }

    [JsonPropertyName("peakToday")]
    public int PeakToday { get; set; }
}
