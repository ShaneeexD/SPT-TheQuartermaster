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

    // Generic enemy target for KillEnemy objectives (Savage, AnyPmc, exUsec, bossBully, etc.)
    [FirestoreProperty("target")]
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    // Advanced kill conditions
    [FirestoreProperty("weapon_tpls")]
    [JsonPropertyName("weapon_tpls")]
    public List<string> WeaponTpls { get; set; } = [];

    [FirestoreProperty("wearing")]
    [JsonPropertyName("wearing")]
    public List<string> Wearing { get; set; } = [];

    [FirestoreProperty("not_wearing")]
    [JsonPropertyName("not_wearing")]
    public List<string> NotWearing { get; set; } = [];

    [FirestoreProperty("body_part")]
    [JsonPropertyName("body_part")]
    public List<string> BodyPart { get; set; } = [];

    [FirestoreProperty("min_distance")]
    [JsonPropertyName("min_distance")]
    public int? MinDistance { get; set; }

    [FirestoreProperty("max_distance")]
    [JsonPropertyName("max_distance")]
    public int? MaxDistance { get; set; }

    [FirestoreProperty("time_from")]
    [JsonPropertyName("time_from")]
    public int? TimeFrom { get; set; }

    [FirestoreProperty("time_to")]
    [JsonPropertyName("time_to")]
    public int? TimeTo { get; set; }

    [FirestoreProperty("one_session_only")]
    [JsonPropertyName("one_session_only")]
    public bool OneSessionOnly { get; set; }

    // Find item / handover pairing
    [FirestoreProperty("count_in_raid")]
    [JsonPropertyName("count_in_raid")]
    public bool CountInRaid { get; set; }

    [FirestoreProperty("handover_after_find")]
    [JsonPropertyName("handover_after_find")]
    public bool HandoverAfterFind { get; set; } = true;

    // Zone / placement objectives
    [FirestoreProperty("zone_id")]
    [JsonPropertyName("zone_id")]
    public string? ZoneId { get; set; }

    [FirestoreProperty("plant_item_tpl")]
    [JsonPropertyName("plant_item_tpl")]
    public string? PlantItemTpl { get; set; }

    [FirestoreProperty("plant_time")]
    [JsonPropertyName("plant_time")]
    public int? PlantTime { get; set; }

    [FirestoreProperty("required_extract")]
    [JsonPropertyName("required_extract")]
    public string? RequiredExtract { get; set; }
}
