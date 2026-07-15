using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Grpc.Core;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using Version = SemanticVersioning.Version;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class FirestoreService(
    ISptLogger<FirestoreService> logger,
    ConfigService configService,
    FirebaseAuthService firebaseAuthService
)
{
    private FirestoreDb? _db;
    public bool IsEnabled { get; private set; }
    public FirestoreDb? Db => _db;

    public async Task InitialiseAsync()
    {
        if (!configService.Config.ModEnabled)
        {
            IsEnabled = false;
            logger.DebugWarning("[TheQuartermaster] Firestore disabled (mod disabled).");
            return;
        }

        var projectId = configService.Config.FirebaseProjectId;
        var apiKey = configService.Config.FirebaseApiKey;

        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(apiKey))
        {
            IsEnabled = false;
            logger.Error("[TheQuartermaster] Firestore public config missing project_id/api_key.");
            return;
        }

        try
        {
            await firebaseAuthService.InitialiseAsync();

            var firestoreClient = await new FirestoreClientBuilder
            {
                ChannelCredentials = ChannelCredentials.SecureSsl
            }.BuildAsync();

            _db = FirestoreDb.Create(projectId, firestoreClient);
            IsEnabled = true;
            logger.DebugInfo($"[TheQuartermaster] Firestore initialised for project {projectId} with no credentials (open rules).");
        }
        catch (Exception ex)
        {
            IsEnabled = false;
            logger.Error($"[TheQuartermaster] Firestore initialisation failed: {ex.Message}", ex);
        }
    }

    public async Task<bool> CheckModVersionAsync()
    {
        if (!IsEnabled || _db is null)
        {
            return true;
        }

        try
        {
            var docRef = _db
                .Collection(QuartermasterConstants.FirestoreCollections.Config)
                .Document(QuartermasterConstants.FirestoreConfig.ModVersion);

            var snapshot = await docRef.GetSnapshotAsync();
            if (!snapshot.Exists)
            {
                logger.DebugInfo("[TheQuartermaster] No mod version requirement found in Firestore; no gate applied.");
                return true;
            }

            if (!snapshot.TryGetValue<string>("minimum_required_mod_version", out var requiredVersionString) ||
                string.IsNullOrWhiteSpace(requiredVersionString))
            {
                logger.DebugInfo("[TheQuartermaster] Mod version doc found but no minimum_required_mod_version field; no gate applied.");
                return true;
            }

            if (!Version.TryParse(requiredVersionString, out var requiredVersion) || requiredVersion is null)
            {
                logger.Error($"[TheQuartermaster] Invalid minimum_required_mod_version in Firestore: {requiredVersionString}. Disabling mod.");
                return false;
            }

            var currentVersion = QuartermasterConstants.Versions.Current;
            if (currentVersion.CompareTo(requiredVersion) < 0)
            {
                logger.Error($"[TheQuartermaster] Mod version {currentVersion} is older than required minimum {requiredVersion}. Disabling mod.");
                return false;
            }

            logger.DebugInfo($"[TheQuartermaster] Mod version {currentVersion} satisfies required minimum {requiredVersion}.");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to check mod version in Firestore: {ex.Message}. Disabling mod.", ex);
            return false;
        }
    }
}
