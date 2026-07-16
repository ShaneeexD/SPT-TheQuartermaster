using Google.Cloud.Firestore;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Services.Contracts;

namespace TheQuartermaster.Server.Services;

[FirestoreData]
public class ListingConfig
{
    [FirestoreProperty("listing_duration_hours")]
    public double ListingDurationHours { get; set; }

    [FirestoreProperty("refresh_cooldown_minutes")]
    public double RefreshCooldownMinutes { get; set; }
}

[Injectable(InjectionType.Singleton)]
public class ListingConfigService(
    ISptLogger<ListingConfigService> logger,
    FirestoreService firestoreService,
    ContractFileService contractFileService
)
{
    private int _listingDurationSeconds = QuartermasterConstants.Marketplace.ListingDurationSeconds;
    private int _refreshCooldownMinutes = QuartermasterConstants.Timings.RefreshCooldownMinutes;

    public int ListingDurationSeconds => _listingDurationSeconds;
    public int RefreshCooldownMinutes => _refreshCooldownMinutes;

    public async Task LoadAsync()
    {
        // Try VM cache first
        if (contractFileService.IsEnabled)
        {
            var fileConfig = await contractFileService.TryGetListingConfigAsync();
            if (fileConfig is not null)
            {
                if (fileConfig.ListingDurationHours > 0)
                {
                    _listingDurationSeconds = (int)(fileConfig.ListingDurationHours * 3600);
                    logger.DebugInfo($"[TheQuartermaster] Loaded listing duration from VM cache: {fileConfig.ListingDurationHours} hours ({_listingDurationSeconds}s).");
                }
                if (fileConfig.RefreshCooldownMinutes > 0)
                {
                    _refreshCooldownMinutes = (int)fileConfig.RefreshCooldownMinutes;
                    logger.DebugInfo($"[TheQuartermaster] Loaded refresh cooldown from VM cache: {fileConfig.RefreshCooldownMinutes} minutes.");
                }
                return;
            }
        }

        // Fall back to Firestore
        if (!firestoreService.IsEnabled || firestoreService.Db is null)
        {
            logger.DebugWarning("[TheQuartermaster] Firestore unavailable; using default listing config.");
            return;
        }

        try
        {
            var docRef = firestoreService.Db
                .Collection(QuartermasterConstants.FirestoreCollections.Config)
                .Document(QuartermasterConstants.FirestoreConfig.ListingConfig);

            var snapshot = await docRef.GetSnapshotAsync();
            if (!snapshot.Exists)
            {
                logger.DebugInfo("[TheQuartermaster] No listing config found; using defaults.");
                return;
            }

            var config = snapshot.ConvertTo<ListingConfig>();

            if (config.ListingDurationHours > 0)
            {
                _listingDurationSeconds = (int)(config.ListingDurationHours * 3600);
                logger.DebugInfo($"[TheQuartermaster] Loaded listing duration from Firestore: {config.ListingDurationHours} hours ({_listingDurationSeconds}s).");
            }
            else
            {
                logger.DebugInfo("[TheQuartermaster] listing_duration_hours missing or invalid; using default.");
            }

            if (config.RefreshCooldownMinutes > 0)
            {
                _refreshCooldownMinutes = (int)config.RefreshCooldownMinutes;
                logger.DebugInfo($"[TheQuartermaster] Loaded refresh cooldown from Firestore: {config.RefreshCooldownMinutes} minutes.");
            }
            else
            {
                logger.DebugInfo("[TheQuartermaster] refresh_cooldown_minutes missing or invalid; using default.");
            }
        }
        catch (Exception ex)
        {
            if (FirestoreService.IsQuotaExhausted(ex))
                logger.Warning("[TheQuartermaster] Failed to load listing config, Firestore quota exhausted. Using defaults.");
            else
                logger.Error($"[TheQuartermaster] Failed to load listing config: {ex.Message}", ex);
        }
    }
}
