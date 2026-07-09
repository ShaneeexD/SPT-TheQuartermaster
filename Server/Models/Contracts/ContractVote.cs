using Google.Cloud.Firestore;
using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Contracts;

[FirestoreData]
public class ContractVote
{
    [FirestoreDocumentId]
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [FirestoreProperty("contract_id")]
    [JsonPropertyName("contract_id")]
    public string ContractId { get; set; } = string.Empty;

    [FirestoreProperty("voter_hash")]
    [JsonPropertyName("voter_hash")]
    public string VoterHash { get; set; } = string.Empty;

    [FirestoreProperty("is_upvote")]
    [JsonPropertyName("is_upvote")]
    public bool IsUpvote { get; set; }

    [FirestoreProperty("voted_at")]
    [JsonPropertyName("voted_at")]
    public Timestamp? VotedAt { get; set; }
}
