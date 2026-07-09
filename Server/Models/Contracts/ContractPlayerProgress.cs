using Google.Cloud.Firestore;
using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Contracts;

public static class ContractPlayerStatus
{
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Expired = "expired";
    public const string Abandoned = "abandoned";
}

[FirestoreData]
public class ContractPlayerProgress
{
    [FirestoreDocumentId]
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [FirestoreProperty("schedule_entry_id")]
    [JsonPropertyName("schedule_entry_id")]
    public string ScheduleEntryId { get; set; } = string.Empty;

    [FirestoreProperty("profile_id_hash")]
    [JsonPropertyName("profile_id_hash")]
    public string ProfileIdHash { get; set; } = string.Empty;

    [FirestoreProperty("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = ContractPlayerStatus.InProgress;

    [FirestoreProperty("counters")]
    [JsonPropertyName("counters")]
    public Dictionary<string, int> Counters { get; set; } = new();

    [FirestoreProperty("completed_at")]
    [JsonPropertyName("completed_at")]
    public Timestamp? CompletedAt { get; set; }

    [FirestoreProperty("updated_at")]
    [JsonPropertyName("updated_at")]
    public Timestamp? UpdatedAt { get; set; }
}
