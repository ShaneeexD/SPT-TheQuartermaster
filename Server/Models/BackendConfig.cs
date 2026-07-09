using Google.Cloud.Firestore;
using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models;

[FirestoreData]
public class BackendConfig
{
    // Community contracts
    [FirestoreProperty("community_contracts_enabled")]
    [JsonPropertyName("community_contracts_enabled")]
    public bool CommunityContractsEnabled { get; set; } = true;

    [FirestoreProperty("approval_percentage")]
    [JsonPropertyName("approval_percentage")]
    public double ApprovalPercentage { get; set; } = 70.0;

    [FirestoreProperty("minimum_votes")]
    [JsonPropertyName("minimum_votes")]
    public int MinimumVotes { get; set; } = 20;

    [FirestoreProperty("voting_hours")]
    [JsonPropertyName("voting_hours")]
    public int VotingHours { get; set; } = 48;

    [FirestoreProperty("max_active_daily_contracts")]
    [JsonPropertyName("max_active_daily_contracts")]
    public int MaxActiveDailyContracts { get; set; } = 1;

    [FirestoreProperty("max_active_weekly_contracts")]
    [JsonPropertyName("max_active_weekly_contracts")]
    public int MaxActiveWeeklyContracts { get; set; } = 1;

    [FirestoreProperty("allow_auto_scheduling")]
    [JsonPropertyName("allow_auto_scheduling")]
    public bool AllowAutoScheduling { get; set; } = true;

    [FirestoreProperty("spt_version")]
    [JsonPropertyName("spt_version")]
    public string SptVersion { get; set; } = "4.0.13";

    // Marketplace
    [FirestoreProperty("base_markup_percent")]
    [JsonPropertyName("base_markup_percent")]
    public double BaseMarkupPercent { get; set; } = 15.0;

    [FirestoreProperty("vanilla_items_only")]
    [JsonPropertyName("vanilla_items_only")]
    public bool VanillaItemsOnly { get; set; } = false;
}
