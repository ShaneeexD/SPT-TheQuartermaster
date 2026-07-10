using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Grpc.Core;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;

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
            logger.Warning("[TheQuartermaster] Firestore disabled (mod disabled).");
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
            logger.Info($"[TheQuartermaster] Firestore initialised for project {projectId} with no credentials (open rules).");
        }
        catch (Exception ex)
        {
            IsEnabled = false;
            logger.Error($"[TheQuartermaster] Firestore initialisation failed: {ex.Message}", ex);
        }
    }
}
