using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Cloud.Firestore;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;
using TheQuartermaster.Server.Models.Contracts;
using TheQuartermaster.Server.Models.Rewards;

namespace TheQuartermaster.Server.Services.Contracts;

/// <summary>
/// DTO for the bundled JSON file served by the Oracle VM.
/// </summary>
internal class ContractDataBundle
{
    [JsonPropertyName("version")]
    public long Version { get; set; }

    [JsonPropertyName("generated_at")]
    public Timestamp? GeneratedAt { get; set; }

    [JsonPropertyName("definitions")]
    public List<ContractDefinition> Definitions { get; set; } = [];

    [JsonPropertyName("schedule_entries")]
    public List<ContractScheduleEntry> ScheduleEntries { get; set; } = [];

    [JsonPropertyName("submissions")]
    public List<ContractSubmission> Submissions { get; set; } = [];

    [JsonPropertyName("backend_config")]
    public BackendConfig? BackendConfig { get; set; }

    [JsonPropertyName("mod_version")]
    public ModVersionData? ModVersion { get; set; }

    [JsonPropertyName("listing_config")]
    public ListingConfigData? ListingConfig { get; set; }

    [JsonPropertyName("item_overrides")]
    public List<ItemPriceOverride> ItemOverrides { get; set; } = [];

    [JsonPropertyName("weeklyReward")]
    public WeeklyReward WeeklyReward { get; set; } = new();

    [JsonPropertyName("communityStats")]
    public CommunityStats CommunityStats { get; set; } = new();
}

public class ModVersionData
{
    [JsonPropertyName("minimum_required_mod_version")]
    public string? MinimumRequiredModVersion { get; set; }
}

public class ListingConfigData
{
    [JsonPropertyName("listing_duration_hours")]
    public double ListingDurationHours { get; set; }

    [JsonPropertyName("refresh_cooldown_minutes")]
    public double RefreshCooldownMinutes { get; set; }
}

/// <summary>
/// Fetches contract data from a static JSON file served over HTTP
/// (e.g. by nginx on an Oracle Cloud VM). Falls back to Firestore
/// if the HTTP request fails or the URL is not configured.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class ContractFileService(
    ISptLogger<ContractFileService> logger,
    ConfigService configService
)
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TimestampJsonConverter(), new NullableTimestampJsonConverter() }
    };

    private readonly object _lock = new();
    private ContractDataBundle? _cachedBundle;
    private DateTime _cachedAt = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(configService.Config.ContractFileUrl);

    private string? FileUrl => configService.Config.ContractFileUrl;

    private bool IsCacheFresh => _cachedBundle is not null && DateTime.UtcNow - _cachedAt < CacheTtl;

    /// <summary>
    /// Try to get the contract version from the file service.
    /// Returns null if unavailable (caller should fall back to Firestore).
    /// </summary>
    public async Task<long?> TryGetContractVersionAsync()
    {
        var bundle = await TryGetBundleAsync();
        return bundle?.Version;
    }

    /// <summary>
    /// Try to get all contract definitions from the file service.
    /// Returns null if unavailable (caller should fall back to Firestore).
    /// </summary>
    public async Task<List<ContractDefinition>?> TryGetDefinitionsAsync()
    {
        var bundle = await TryGetBundleAsync();
        return bundle?.Definitions;
    }

    /// <summary>
    /// Try to get all schedule entries from the file service.
    /// Returns null if unavailable (caller should fall back to Firestore).
    /// </summary>
    public async Task<List<ContractScheduleEntry>?> TryGetScheduleEntriesAsync()
    {
        var bundle = await TryGetBundleAsync();
        return bundle?.ScheduleEntries;
    }

    /// <summary>
    /// Try to get all submissions from the file service.
    /// Returns null if unavailable (caller should fall back to Firestore).
    /// </summary>
    public async Task<List<ContractSubmission>?> TryGetSubmissionsAsync()
    {
        var bundle = await TryGetBundleAsync();
        return bundle?.Submissions;
    }

    /// <summary>
    /// Try to get the backend config from the file service.
    /// Returns null if unavailable (caller should fall back to Firestore).
    /// </summary>
    public async Task<BackendConfig?> TryGetBackendConfigAsync()
    {
        var bundle = await TryGetBundleAsync();
        return bundle?.BackendConfig;
    }

    /// <summary>
    /// Try to get the mod version data from the file service.
    /// Returns null if unavailable (caller should fall back to Firestore).
    /// </summary>
    public async Task<ModVersionData?> TryGetModVersionAsync()
    {
        var bundle = await TryGetBundleAsync();
        return bundle?.ModVersion;
    }

    /// <summary>
    /// Try to get the listing config from the file service.
    /// Returns null if unavailable (caller should fall back to Firestore).
    /// </summary>
    public async Task<ListingConfigData?> TryGetListingConfigAsync()
    {
        var bundle = await TryGetBundleAsync();
        return bundle?.ListingConfig;
    }

    /// <summary>
    /// Try to get item price overrides from the file service.
    /// Returns null if unavailable (caller should fall back to Firestore).
    /// </summary>
    public async Task<List<ItemPriceOverride>?> TryGetItemOverridesAsync()
    {
        var bundle = await TryGetBundleAsync();
        return bundle?.ItemOverrides;
    }

    internal async Task<ContractDataBundle?> TryGetBundleAsync()
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(FileUrl))
        {
            return null;
        }

        lock (_lock)
        {
            if (IsCacheFresh)
            {
                return _cachedBundle;
            }
        }

        try
        {
            using var response = await _httpClient.GetAsync(FileUrl);

            if (!response.IsSuccessStatusCode)
            {
                logger.DebugWarning($"[TheQuartermaster] Contract file fetch failed: {(int)response.StatusCode} {response.StatusCode}");
                return GetStaleCacheIfAvailable();
            }

            var json = await response.Content.ReadAsStringAsync();
            var bundle = JsonSerializer.Deserialize<ContractDataBundle>(json, JsonOptions);

            if (bundle is null)
            {
                logger.DebugWarning("[TheQuartermaster] Contract file deserialized to null.");
                return GetStaleCacheIfAvailable();
            }

            lock (_lock)
            {
                _cachedBundle = bundle;
                _cachedAt = DateTime.UtcNow;
            }

            logger.DebugInfo($"[TheQuartermaster] Contract file fetched from {FileUrl}: version={bundle.Version}, {bundle.Definitions.Count} definitions, {bundle.ScheduleEntries.Count} schedule entries, {bundle.Submissions.Count} submissions.");
            return bundle;
        }
        catch (Exception ex)
        {
            logger.DebugWarning($"[TheQuartermaster] Contract file fetch error: {ex.Message}");
            return GetStaleCacheIfAvailable();
        }
    }

    private ContractDataBundle? GetStaleCacheIfAvailable()
    {
        lock (_lock)
        {
            if (_cachedBundle is not null)
            {
                logger.DebugDebug("[TheQuartermaster] Using stale contract file cache as fallback.");
                return _cachedBundle;
            }
        }
        return null;
    }
}
