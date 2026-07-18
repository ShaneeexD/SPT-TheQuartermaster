using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Rewards;

public class RewardPackage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("contents")]
    public List<RewardPackageContent> Contents { get; set; } = [];
}
