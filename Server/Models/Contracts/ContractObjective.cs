using Google.Cloud.Firestore;
using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Contracts;

[FirestoreData]
public class ContractObjective
{
    [FirestoreDocumentId]
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [FirestoreProperty("type")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [FirestoreProperty("description")]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [FirestoreProperty("target_tpl")]
    [JsonPropertyName("target_tpl")]
    public string? TargetTpl { get; set; }

    [FirestoreProperty("target_map")]
    [JsonPropertyName("target_map")]
    public string? TargetMap { get; set; }

    [FirestoreProperty("target_zone")]
    [JsonPropertyName("target_zone")]
    public string? TargetZone { get; set; }

    [FirestoreProperty("count")]
    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    [FirestoreProperty("required_in_raid")]
    [JsonPropertyName("required_in_raid")]
    public bool RequiredInRaid { get; set; }

    [FirestoreProperty("target_faction")]
    [JsonPropertyName("target_faction")]
    public string? TargetFaction { get; set; }
}
