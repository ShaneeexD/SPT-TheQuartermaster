using System.Text.Json;
using System.Text.Json.Serialization;
using TheQuartermaster.Server;

namespace TheQuartermaster.Server.Models;

public class RtdbListing
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("seller_hash")]
    public string? SellerHash { get; set; }

    [JsonPropertyName("seller_uid")]
    public string? SellerUid { get; set; }

    [JsonPropertyName("seller_name")]
    public string? SellerName { get; set; }

    [JsonPropertyName("buyer_hash")]
    public string? BuyerHash { get; set; }

    [JsonPropertyName("buyer_uid")]
    public string? BuyerUid { get; set; }

    [JsonPropertyName("root_tpl")]
    public string? RootTpl { get; set; }

    [JsonPropertyName("root_name")]
    public string? RootName { get; set; }

    [JsonPropertyName("short_name")]
    public string? ShortName { get; set; }

    [JsonPropertyName("item_tree")]
    public string? ItemTreeJson { get; set; }

    [JsonPropertyName("required_tpls")]
    public List<string> RequiredTpls { get; set; } = [];

    [JsonPropertyName("base_price")]
    public double BasePrice { get; set; }

    [JsonPropertyName("market_price")]
    public double MarketPrice { get; set; }

    [JsonPropertyName("quality_multiplier")]
    public double QualityMultiplier { get; set; } = 1.0;

    [JsonPropertyName("is_vanilla")]
    public bool IsVanilla { get; set; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("sold_at")]
    public long SoldAt { get; set; }

    [JsonPropertyName("server_id")]
    public string? ServerId { get; set; }

    [JsonPropertyName("listing_metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public class RtdbListingState
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("buyer_uid")]
    public string? BuyerUid { get; set; }

    [JsonPropertyName("buyer_hash")]
    public string? BuyerHash { get; set; }

    [JsonPropertyName("sold_at")]
    public long? SoldAt { get; set; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("remaining_quantity")]
    public int RemainingQuantity { get; set; } = 1;

    [JsonPropertyName("last_purchase_id")]
    public string? LastPurchaseId { get; set; }

    [JsonPropertyName("last_purchase_quantity")]
    public int LastPurchaseQuantity { get; set; }

    [JsonPropertyName("last_purchase_status")]
    public string? LastPurchaseStatus { get; set; }

    [JsonPropertyName("last_purchase_expires_at")]
    public long? LastPurchaseExpiresAt { get; set; }
}

public class RtdbCatalogueMeta
{
    [JsonPropertyName("version")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Version { get; set; }

    [JsonPropertyName("generated_at")]
    public long GeneratedAt { get; set; }

    [JsonPropertyName("page_count")]
    public int PageCount { get; set; }

    [JsonPropertyName("listing_count")]
    public int ListingCount { get; set; }
}

public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt64().ToString();
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        return JsonSerializer.Deserialize<string>(ref reader, options);
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}

public class RtdbCataloguePage
{
    [JsonPropertyName("page_id")]
    public string? PageId { get; set; }

    [JsonPropertyName("catalogue_version")]
    public string? CatalogueVersion { get; set; }

    [JsonPropertyName("generated_at")]
    public long GeneratedAt { get; set; }

    [JsonPropertyName("listings")]
    public List<RtdbListing> Listings { get; set; } = [];
}

public class RtdbWorkerLease
{
    [JsonPropertyName("owner_uid")]
    public string? OwnerUid { get; set; }

    [JsonPropertyName("acquired_at")]
    public long AcquiredAt { get; set; }

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }
}

public class RtdbCatalogueRebuild
{
    [JsonPropertyName("pending_new_count")]
    public int PendingNewCount { get; set; }

    [JsonPropertyName("pending_sold_count")]
    public int PendingSoldCount { get; set; }

    [JsonPropertyName("last_rebuild_at")]
    public long LastRebuildAt { get; set; }
}

public class RtdbCatalogueCache
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("generated_at")]
    public long GeneratedAt { get; set; }

    [JsonPropertyName("page_count")]
    public int PageCount { get; set; }

    [JsonPropertyName("listings")]
    public List<RtdbListing> Listings { get; set; } = [];

    [JsonPropertyName("states")]
    public Dictionary<string, RtdbListingState> States { get; set; } = new();
}

public class RtdbBuyFilters
{
    [JsonPropertyName("buy_categories")]
    public List<string> BuyCategories { get; set; } = [];

    [JsonPropertyName("buy_items")]
    public List<string> BuyItems { get; set; } = [];

    [JsonPropertyName("buy_prohibited_categories")]
    public List<string> BuyProhibitedCategories { get; set; } = [];

    [JsonPropertyName("buy_prohibited_items")]
    public List<string> BuyProhibitedItems { get; set; } = new(QuartermasterConstants.ExcludedTpls);
}

public class RtdbListingLimits
{
    [JsonPropertyName("default_max_quantity")]
    public int DefaultMaxQuantity { get; set; } = 10;

    [JsonPropertyName("max_quantity_overrides")]
    public Dictionary<string, int> MaxQuantityOverrides { get; set; } = new();
}
