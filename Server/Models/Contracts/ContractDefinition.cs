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

    [FirestoreProperty("admin_blocked")]
    [JsonPropertyName("admin_blocked")]
    public bool AdminBlocked { get; set; }

    [FirestoreProperty("validation_errors")]
    [JsonPropertyName("validation_errors")]
    public List<string> ValidationErrors { get; set; } = [];

    [FirestoreProperty("metadata")]
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}
