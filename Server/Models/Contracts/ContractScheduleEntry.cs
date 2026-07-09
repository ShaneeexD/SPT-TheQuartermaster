using Google.Cloud.Firestore;
using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Contracts;

public static class ContractRecurrenceType
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Weekend = "weekend";
    public const string OneTime = "one_time";
}

[FirestoreData]
public class ContractScheduleEntry
{
    [FirestoreDocumentId]
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [FirestoreProperty("contract_definition_id")]
    [JsonPropertyName("contract_definition_id")]
    public string ContractDefinitionId { get; set; } = string.Empty;

    [FirestoreProperty("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = ContractStatus.Scheduled;

    [FirestoreProperty("recurrence_type")]
    [JsonPropertyName("recurrence_type")]
    public string RecurrenceType { get; set; } = ContractRecurrenceType.OneTime;

    [FirestoreProperty("start_at")]
    [JsonPropertyName("start_at")]
    public Timestamp? StartAt { get; set; }

    [FirestoreProperty("end_at")]
    [JsonPropertyName("end_at")]
    public Timestamp? EndAt { get; set; }

    [FirestoreProperty("activated_at")]
    [JsonPropertyName("activated_at")]
    public Timestamp? ActivatedAt { get; set; }

    [FirestoreProperty("expires_at")]
    [JsonPropertyName("expires_at")]
    public Timestamp? ExpiresAt { get; set; }

    [FirestoreProperty("admin_created")]
    [JsonPropertyName("admin_created")]
    public bool AdminCreated { get; set; }

    [FirestoreProperty("created_at")]
    [JsonPropertyName("created_at")]
    public Timestamp? CreatedAt { get; set; }

    [FirestoreProperty("quest_id")]
    [JsonPropertyName("quest_id")]
    public string? QuestId { get; set; }
}
