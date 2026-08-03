using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Reflection.Patching;
using TheQuartermaster.Server.Patches;
using TheQuartermaster.Server.Services;
using TheQuartermaster.Server.Services.Contracts;
using Version = SemanticVersioning.Version;
using Range = SemanticVersioning.Range;

namespace TheQuartermaster.Server;

public record QuartermasterMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.shaneeexd.thequartermaster";
    public string Name { get; init; } = "The Quartermaster";
    public string Author { get; init; } = "ShaneeexD";
    public List<string>? Contributors { get; init; } = null;
    public Version Version { get; init; } = new Version("1.4.2");
    public Range SptVersion { get; init; } = new Range("~4.1.0");
    public List<string>? Incompatibilities { get; init; } = null;
    public Dictionary<string, Range>? ModDependencies { get; init; } = null;
    public string? Url { get; init; } = null;
    public bool HasPrepatcher { get; init; } = false;
    public string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class QuartermasterPlugin(
    ISptLogger<QuartermasterPlugin> logger,
    ModHelper modHelper,
    ConfigService configService,
    VanillaAllowlistService vanillaAllowlistService,
    FirestoreService firestoreService,
    MarketplaceService marketplaceService,
    MarketplaceWorkerService marketplaceWorkerService,
    TraderService traderService,
    BackendConfigService backendConfigService,
    ListingConfigService listingConfigService,
    CommunityContractService communityContractService,
    WorkshopContractSyncService workshopContractSyncService,
    HardcodedShipmentService hardcodedShipmentService,
    PresenceHeartbeatService presenceHeartbeatService,
    IEnumerable<IRuntimePatch> patches
) : IOnLoad
{
    private static string _modPath = string.Empty;
    public static string ModPath => _modPath;

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            _modPath = modHelper.GetAbsolutePathToModFolder(typeof(QuartermasterPlugin).Assembly);
            logger.DebugInfo("[TheQuartermaster] Initialising...");

            configService.Load(_modPath);
            vanillaAllowlistService.Load(_modPath);
            await firestoreService.InitialiseAsync();
            if (!await firestoreService.CheckModVersionAsync())
            {
                configService.Config.ModEnabled = false;
                logger.Error("[TheQuartermaster] Mod version mismatch; disabling The Quartermaster.");
                return;
            }

            await marketplaceService.InitialiseAsync();
            await backendConfigService.LoadAsync();
            await listingConfigService.LoadAsync();

            // Ensure hardcoded shipment crate templates exist before the trader assort is built.
            hardcodedShipmentService.EnsureTemplates();

            await traderService.RegisterTrader(_modPath);

            // Initial population: pull approved contracts from the workshop, schedule active slots,
            // then inject the resulting quests before the first profile loads.
            try
            {
                await workshopContractSyncService.SyncAsync();
            }
            catch (Exception ex)
            {
                logger.DebugWarning($"[TheQuartermaster] Initial workshop sync failed: {ex.Message}");
            }

            await communityContractService.RefreshAsync(force: true);
            communityContractService.Start();
            workshopContractSyncService.Start();
            marketplaceWorkerService.Start();
            presenceHeartbeatService.Start();

            // Enable all patches from this assembly (DI-managed in 4.1)
            foreach (var patch in patches)
            {
                patch.Enable();
            }

            logger.DebugInfo("[TheQuartermaster] Loaded successfully.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to load: {ex}", ex);
            throw;
        }
    }
}
