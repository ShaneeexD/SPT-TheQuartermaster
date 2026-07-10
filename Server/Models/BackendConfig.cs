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

    [FirestoreProperty("max_active_special_contracts")]
    [JsonPropertyName("max_active_special_contracts")]
    public int MaxActiveSpecialContracts { get; set; } = 2;

    [FirestoreProperty("allow_auto_scheduling")]
    [JsonPropertyName("allow_auto_scheduling")]
    public bool AllowAutoScheduling { get; set; } = true;

    [FirestoreProperty("daily_contract_duration_hours")]
    [JsonPropertyName("daily_contract_duration_hours")]
    public int DailyContractDurationHours { get; set; } = 24;

    [FirestoreProperty("weekly_contract_duration_hours")]
    [JsonPropertyName("weekly_contract_duration_hours")]
    public int WeeklyContractDurationHours { get; set; } = 168;

    [FirestoreProperty("community_contract_cooldown_days")]
    [JsonPropertyName("community_contract_cooldown_days")]
    public int CommunityContractCooldownDays { get; set; } = 14;

    [FirestoreProperty("allow_repeat_templates")]
    [JsonPropertyName("allow_repeat_templates")]
    public bool AllowRepeatTemplates { get; set; } = true;

    [FirestoreProperty("max_pending_submissions_per_user")]
    [JsonPropertyName("max_pending_submissions_per_user")]
    public int MaxPendingSubmissionsPerUser { get; set; } = 3;

    [FirestoreProperty("max_submissions_per_day")]
    [JsonPropertyName("max_submissions_per_day")]
    public int MaxSubmissionsPerDay { get; set; } = 2;

    [FirestoreProperty("spt_version")]
    [JsonPropertyName("spt_version")]
    public string SptVersion { get; set; } = "4.0.13";

    // Workshop sync
    [FirestoreProperty("workshop_sync_enabled")]
    [JsonPropertyName("workshop_sync_enabled")]
    public bool WorkshopSyncEnabled { get; set; } = true;

    [FirestoreProperty("workshop_api_url")]
    [JsonPropertyName("workshop_api_url")]
    public string WorkshopApiUrl { get; set; } = "https://serenity-workshop.netlify.app/api/contract-list";

    // Marketplace
    [FirestoreProperty("base_markup_percent")]
    [JsonPropertyName("base_markup_percent")]
    public double BaseMarkupPercent { get; set; } = 15.0;

    [FirestoreProperty("vanilla_items_only")]
    [JsonPropertyName("vanilla_items_only")]
    public bool VanillaItemsOnly { get; set; } = false;

    [FirestoreProperty("max_active_listings")]
    [JsonPropertyName("max_active_listings")]
    public int MaxActiveListings { get; set; } = 200;
}
