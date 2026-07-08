using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models;

public record QuartermasterConfig
{
    [JsonPropertyName("modEnabled")]
    public bool ModEnabled { get; set; } = true;

    [JsonPropertyName("uploadConsent")]
    public bool UploadConsent { get; set; } = true;

    [JsonPropertyName("baseMarkupPercent")]
    public double BaseMarkupPercent { get; set; } = 15.0;

    [JsonPropertyName("minPrice")]
    public int MinPrice { get; set; } = 100;

    [JsonPropertyName("maxPrice")]
    public int MaxPrice { get; set; } = 50_000_000;

    [JsonPropertyName("maxListingsPerPlayer")]
    public int MaxListingsPerPlayer { get; set; } = 100;

    [JsonPropertyName("listingDurationHours")]
    public int ListingDurationHours { get; set; } = 168;

    [JsonPropertyName("maxItemTreeSize")]
    public int MaxItemTreeSize { get; set; } = 100;

    [JsonPropertyName("debugLogging")]
    public bool DebugLogging { get; set; } = false;

    [JsonPropertyName("vanillaItemsOnly")]
    public bool VanillaItemsOnly { get; set; } = false;

    [JsonPropertyName("sellerAnonymizationSalt")]
    public string SellerAnonymizationSalt { get; set; } = string.Empty;

    public int GetListingDurationSeconds() => ListingDurationHours * 3600;
}
