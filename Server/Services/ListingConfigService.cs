using Google.Cloud.Firestore;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Services.Contracts;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class ListingConfigService(
    ISptLogger<ListingConfigService> logger,
    FirestoreService firestoreService
)
{
    private int _listingDurationSeconds = QuartermasterConstants.Marketplace.ListingDurationSeconds;

    public int ListingDurationSeconds => _listingDurationSeconds;

    public async Task LoadAsync()
    {
        if (!firestoreService.IsEnabled || firestoreService.Db is null)
        {
            logger.DebugWarning("[TheQuartermaster] Firestore unavailable; using default listing duration.");
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
                logger.DebugInfo("[TheQuartermaster] No listing config found; using default listing duration.");
                return;
            }

            if (snapshot.TryGetValue<double>("listing_duration_hours", out var hours) && hours > 0)
            {
                _listingDurationSeconds = (int)(hours * 3600);
                logger.DebugInfo($"[TheQuartermaster] Loaded listing duration from Firestore: {hours} hours ({_listingDurationSeconds}s).");
            }
            else
            {
                logger.DebugInfo("[TheQuartermaster] listing_duration_hours missing or invalid; using default.");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to load listing config: {ex.Message}", ex);
        }
    }
}
