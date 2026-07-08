using Google.Cloud.Firestore;
using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models;

[FirestoreData]
public class QuartermasterListing
{
    [FirestoreDocumentId]
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [FirestoreProperty("seller_hash")]
    [JsonPropertyName("seller_hash")]
    public string? SellerHash { get; set; }

    [FirestoreProperty("buyer_hash")]
    [JsonPropertyName("buyer_hash")]
    public string? BuyerHash { get; set; }

    [FirestoreProperty("root_tpl")]
    [JsonPropertyName("root_tpl")]
    public string? RootTpl { get; set; }

    [FirestoreProperty("root_name")]
    [JsonPropertyName("root_name")]
    public string? RootName { get; set; }

    [FirestoreProperty("short_name")]
    [JsonPropertyName("short_name")]
    public string? ShortName { get; set; }

    [FirestoreProperty("item_tree")]
    [JsonPropertyName("item_tree")]
    public string? ItemTreeJson { get; set; }

    [FirestoreProperty("required_tpls")]
    [JsonPropertyName("required_tpls")]
    public List<string> RequiredTpls { get; set; } = [];

    [FirestoreProperty("base_price")]
    [JsonPropertyName("base_price")]
    public double BasePrice { get; set; }

    [FirestoreProperty("market_price")]
    [JsonPropertyName("market_price")]
    public double MarketPrice { get; set; }

    [FirestoreProperty("quality_multiplier")]
    [JsonPropertyName("quality_multiplier")]
    public double QualityMultiplier { get; set; } = 1.0;

    [FirestoreProperty("is_vanilla")]
    [JsonPropertyName("is_vanilla")]
    public bool IsVanilla { get; set; }

    [FirestoreProperty("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";

    [FirestoreProperty("created_at")]
    [JsonPropertyName("created_at")]
    public Timestamp? CreatedAt { get; set; }

    [FirestoreProperty("expires_at")]
    [JsonPropertyName("expires_at")]
    public Timestamp? ExpiresAt { get; set; }

    [FirestoreProperty("sold_at")]
    [JsonPropertyName("sold_at")]
    public Timestamp? SoldAt { get; set; }

    [FirestoreProperty("server_id")]
    [JsonPropertyName("server_id")]
    public string? ServerId { get; set; }

    [FirestoreProperty("listing_metadata")]
    [JsonPropertyName("listing_metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public static class ListingStatus
{
    public const string Active = "active";
    public const string Sold = "sold";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
}
