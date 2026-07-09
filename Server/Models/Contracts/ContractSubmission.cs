using Google.Cloud.Firestore;
using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Contracts;

[FirestoreData]
public class ContractSubmission
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

    [FirestoreProperty("created_by")]
    [JsonPropertyName("created_by")]
    public string CreatedBy { get; set; } = string.Empty;

    [FirestoreProperty("admin_created")]
    [JsonPropertyName("admin_created")]
    public bool AdminCreated { get; set; }

    [FirestoreProperty("admin_featured")]
    [JsonPropertyName("admin_featured")]
    public bool AdminFeatured { get; set; }

    [FirestoreProperty("spt_version")]
    [JsonPropertyName("spt_version")]
    public string SptVersion { get; set; } = "4.0.13";

    [FirestoreProperty("objectives")]
    [JsonPropertyName("objectives")]
    public List<ContractObjective> Objectives { get; set; } = [];

    [FirestoreProperty("rewards")]
    [JsonPropertyName("rewards")]
    public ContractRewards Rewards { get; set; } = new();

    [FirestoreProperty("duration_hours")]
    [JsonPropertyName("duration_hours")]
    public int DurationHours { get; set; } = 24;

    [FirestoreProperty("upvotes")]
    [JsonPropertyName("upvotes")]
    public int Upvotes { get; set; }

    [FirestoreProperty("downvotes")]
    [JsonPropertyName("downvotes")]
    public int Downvotes { get; set; }

    [FirestoreProperty("approval_ratio")]
    [JsonPropertyName("approval_ratio")]
    public double ApprovalRatio { get; set; }

    [FirestoreProperty("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = ContractStatus.PendingVote;

    [FirestoreProperty("submitted_at")]
    [JsonPropertyName("submitted_at")]
    public Timestamp? SubmittedAt { get; set; }

    [FirestoreProperty("voting_ends_at")]
    [JsonPropertyName("voting_ends_at")]
    public Timestamp? VotingEndsAt { get; set; }

    [FirestoreProperty("admin_blocked")]
    [JsonPropertyName("admin_blocked")]
    public bool AdminBlocked { get; set; }

    [FirestoreProperty("validation_errors")]
    [JsonPropertyName("validation_errors")]
    public List<string> ValidationErrors { get; set; } = [];
}
