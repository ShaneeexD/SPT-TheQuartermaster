using Google.Cloud.Firestore;
using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models.Contracts;

[FirestoreData]
public class ContractRewards
{
    [FirestoreProperty("roubles")]
    [JsonPropertyName("roubles")]
    public int Roubles { get; set; }

    [FirestoreProperty("experience")]
    [JsonPropertyName("experience")]
    public int Experience { get; set; }

    [FirestoreProperty("items")]
    [JsonPropertyName("items")]
    public List<RewardItem> Items { get; set; } = [];

    [FirestoreProperty("trader_standing")]
    [JsonPropertyName("trader_standing")]
    public double TraderStanding { get; set; }
}

[FirestoreData]
public class RewardItem
{
    [FirestoreProperty("tpl")]
    [JsonPropertyName("tpl")]
    public string Tpl { get; set; } = string.Empty;

    [FirestoreProperty("count")]
    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    [FirestoreProperty("found_in_raid")]
    [JsonPropertyName("found_in_raid")]
    public bool FoundInRaid { get; set; }
}
