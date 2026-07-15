using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;

namespace TheQuartermaster.Server.Services;

public class MarketplaceDataBundle
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("generated_at")]
    public long GeneratedAt { get; set; }

    [JsonPropertyName("listings")]
    public Dictionary<string, RtdbListing>? Listings { get; set; }

    [JsonPropertyName("states")]
    public Dictionary<string, RtdbListingState>? States { get; set; }

    [JsonPropertyName("buy_filters")]
    public RtdbBuyFilters? BuyFilters { get; set; }

    [JsonPropertyName("listing_limits")]
    public RtdbListingLimits? ListingLimits { get; set; }
}

[Injectable(InjectionType.Singleton)]
public class MarketplaceFileService(
    ISptLogger<MarketplaceFileService> logger,
    ConfigService configService
)
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _lock = new();
    private MarketplaceDataBundle? _lastBundle;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(configService.Config.MarketplaceFileUrl);

    private string? FileUrl => configService.Config.MarketplaceFileUrl;

    public async Task<MarketplaceDataBundle?> TryGetBundleAsync()
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(FileUrl))
        {
            return null;
        }

        try
        {
            using var response = await _httpClient.GetAsync(FileUrl);

            if (!response.IsSuccessStatusCode)
            {
                logger.DebugWarning($"[TheQuartermaster] Marketplace file fetch failed: {(int)response.StatusCode} {response.StatusCode}");
                return GetStaleCacheIfAvailable();
            }

            var json = await response.Content.ReadAsStringAsync();
            var bundle = JsonSerializer.Deserialize<MarketplaceDataBundle>(json, JsonOptions);

            if (bundle is null)
            {
                logger.DebugWarning("[TheQuartermaster] Marketplace file deserialized to null.");
                return GetStaleCacheIfAvailable();
            }

            lock (_lock)
            {
                _lastBundle = bundle;
            }

            var listingCount = bundle.Listings?.Count ?? 0;
            var stateCount = bundle.States?.Count ?? 0;
            logger.DebugInfo($"[TheQuartermaster] Marketplace file fetched from {FileUrl}: version={bundle.Version}, {listingCount} listings, {stateCount} states.");
            return bundle;
        }
        catch (Exception ex)
        {
            logger.DebugWarning($"[TheQuartermaster] Marketplace file fetch error: {ex.Message}");
            return GetStaleCacheIfAvailable();
        }
    }

    private MarketplaceDataBundle? GetStaleCacheIfAvailable()
    {
        lock (_lock)
        {
            if (_lastBundle is not null)
            {
                logger.DebugDebug("[TheQuartermaster] Using last known marketplace file as fallback.");
                return _lastBundle;
            }
        }
        return null;
    }
}
