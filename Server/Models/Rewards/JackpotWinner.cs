using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Rewards;

public class JackpotWinner
{
    [JsonPropertyName("week")]
    public string Week { get; set; } = string.Empty;

    [JsonPropertyName("winnerHash")]
    public string WinnerHash { get; set; } = string.Empty;

    [JsonPropertyName("winnerName")]
    public string WinnerName { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("generatedAt")]
    public long GeneratedAt { get; set; }
}
