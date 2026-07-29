using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Rewards;

public class JackpotPool
{
    [JsonPropertyName("amount")]
    public long Amount { get; set; }
}
