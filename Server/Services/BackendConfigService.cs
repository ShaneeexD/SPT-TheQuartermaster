using Google.Cloud.Firestore;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;
using TheQuartermaster.Server.Services;

namespace TheQuartermaster.Server.Services.Contracts;

[Injectable(InjectionType.Singleton)]
public class BackendConfigService(
    ISptLogger<BackendConfigService> logger,
    FirestoreService firestoreService
)
{
    private BackendConfig _config = new();

    public BackendConfig Config => _config;

    public async Task LoadAsync()
    {
        if (!firestoreService.IsEnabled || firestoreService.Db is null)
        {
            logger.DebugWarning("[TheQuartermaster] Firestore unavailable; using default backend config.");
            return;
        }

        try
        {
            var docRef = firestoreService.Db
                .Collection(QuartermasterConstants.FirestoreCollections.Config)
                .Document(QuartermasterConstants.FirestoreConfig.ContractConfig);

            var snapshot = await docRef.GetSnapshotAsync();
            if (snapshot.Exists)
            {
                _config = snapshot.ConvertTo<BackendConfig>();
                logger.DebugInfo("[TheQuartermaster] Loaded backend config from Firestore.");
            }
            else
            {
                logger.DebugInfo("[TheQuartermaster] No backend config found; using defaults.");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to load backend config: {ex.Message}", ex);
        }
    }

    public async Task RefreshAsync()
    {
        await LoadAsync();
    }

    public async Task SaveAsync(BackendConfig config)
    {
        if (!firestoreService.IsEnabled || firestoreService.Db is null)
        {
            logger.DebugWarning("[TheQuartermaster] Firestore unavailable; cannot save backend config.");
            return;
        }

        try
        {
            var docRef = firestoreService.Db
                .Collection(QuartermasterConstants.FirestoreCollections.Config)
                .Document(QuartermasterConstants.FirestoreConfig.ContractConfig);

            await docRef.SetAsync(config);
            _config = config;
            logger.DebugInfo("[TheQuartermaster] Saved backend config to Firestore.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to save backend config: {ex.Message}", ex);
        }
    }
}
