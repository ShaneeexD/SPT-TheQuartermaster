using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Rewards;

public class RewardPackageContent
{
    [JsonPropertyName("tpl")]
    public string Tpl { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    [JsonPropertyName("foundInRaid")]
    public bool FoundInRaid { get; set; } = false;
}
