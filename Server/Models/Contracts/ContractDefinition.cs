using Google.Cloud.Firestore;
using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Contracts;

[FirestoreData]
public class ContractDefinition
{
    [FirestoreDocumentId]
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [FirestoreProperty("title")]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [FirestoreProperty("description")]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [FirestoreProperty("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = ContractStatus.Draft;

    [FirestoreProperty("recurrence_type")]
    [JsonPropertyName("recurrence_type")]
    public string RecurrenceType { get; set; } = ContractRecurrenceType.OneTime;

    [FirestoreProperty("created_by")]
    [JsonPropertyName("created_by")]
    public string CreatedBy { get; set; } = string.Empty;

    [FirestoreProperty("author_uid")]
    [JsonPropertyName("author_uid")]
    public string AuthorUid { get; set; } = string.Empty;

    [FirestoreProperty("source")]
    [JsonPropertyName("source")]
    public string Source { get; set; } = "community";

    [FirestoreProperty("admin_created")]
    [JsonPropertyName("admin_created")]
    public bool AdminCreated { get; set; }

    [FirestoreProperty("admin_featured")]
    [JsonPropertyName("admin_featured")]
    public bool AdminFeatured { get; set; }

    [FirestoreProperty("admin_blocked")]
    [JsonPropertyName("admin_blocked")]
    public bool AdminBlocked { get; set; }

    [FirestoreProperty("spt_version")]
    [JsonPropertyName("spt_version")]
    public string SptVersion { get; set; } = "4.0.13";

    [FirestoreProperty("objectives")]
    [JsonPropertyName("objectives")]
    public List<ContractObjective> Objectives { get; set; } = [];

    [FirestoreProperty("rewards")]
    [JsonPropertyName("rewards")]
    public ContractRewards Rewards { get; set; } = new();

    [FirestoreProperty("upvotes")]
    [JsonPropertyName("upvotes")]
    public int Upvotes { get; set; }

    [FirestoreProperty("downvotes")]
    [JsonPropertyName("downvotes")]
    public int Downvotes { get; set; }

    [FirestoreProperty("approval_ratio")]
    [JsonPropertyName("approval_ratio")]
    public double ApprovalRatio { get; set; }

    [FirestoreProperty("created_at")]
    [JsonPropertyName("created_at")]
    public Timestamp? CreatedAt { get; set; }

    [FirestoreProperty("voting_ends_at")]
    [JsonPropertyName("voting_ends_at")]
    public Timestamp? VotingEndsAt { get; set; }

    [FirestoreProperty("approved_at")]
    [JsonPropertyName("approved_at")]
    public Timestamp? ApprovedAt { get; set; }

    [FirestoreProperty("scheduled_start_at")]
    [JsonPropertyName("scheduled_start_at")]
    public Timestamp? ScheduledStartAt { get; set; }

    [FirestoreProperty("scheduled_end_at")]
    [JsonPropertyName("scheduled_end_at")]
    public Timestamp? ScheduledEndAt { get; set; }

    [FirestoreProperty("activated_at")]
    [JsonPropertyName("activated_at")]
    public Timestamp? ActivatedAt { get; set; }

    [FirestoreProperty("expired_at")]
    [JsonPropertyName("expired_at")]
    public Timestamp? ExpiredAt { get; set; }

    [FirestoreProperty("rejected_at")]
    [JsonPropertyName("rejected_at")]
    public Timestamp? RejectedAt { get; set; }

    [FirestoreProperty("last_used_at")]
    [JsonPropertyName("last_used_at")]
    public Timestamp? LastUsedAt { get; set; }

    [FirestoreProperty("updated_at")]
    [JsonPropertyName("updated_at")]
    public Timestamp? UpdatedAt { get; set; }

    [FirestoreProperty("validation_errors")]
    [JsonPropertyName("validation_errors")]
    public List<string> ValidationErrors { get; set; } = [];

    [FirestoreProperty("metadata")]
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}
