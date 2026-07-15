using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Reflection.Patching;
using TheQuartermaster.Server.Patches;
using TheQuartermaster.Server.Services;
using TheQuartermaster.Server.Services.Contracts;
using Version = SemanticVersioning.Version;
using Range = SemanticVersioning.Range;

namespace TheQuartermaster.Server;

public record QuartermasterMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.shaneeexd.thequartermaster";
    public override string Name { get; init; } = "The Quartermaster";
    public override string Author { get; init; } = "ShaneeexD";
    public override List<string>? Contributors { get; init; } = null;
    public override Version Version { get; init; } = QuartermasterConstants.Versions.Current;
    public override Range SptVersion { get; init; } = new Range("~4.0.13");
    public override List<string>? Incompatibilities { get; init; } = null;
    public override Dictionary<string, Range>? ModDependencies { get; init; } = null;
    public override string? Url { get; init; } = null;
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 1)]
public class QuartermasterPlugin(
    ISptLogger<QuartermasterPlugin> logger,
    ISptLogger<SellPatch> sellPatchLogger,
    ISptLogger<BuyPatch> buyPatchLogger,
    ModHelper modHelper,
    ConfigService configService,
    VanillaAllowlistService vanillaAllowlistService,
    FirestoreService firestoreService,
    ListingService listingService,
    ItemCloneService itemCloneService,
    MarketplaceService marketplaceService,
    MarketplaceWorkerService marketplaceWorkerService,
    PurchaseService purchaseService,
    InventoryHelper inventoryHelper,
    TraderService traderService,
    PaymentService paymentService,
    QuestHelper questHelper,
    ItemOverrideService itemOverrideService,
    ItemHelper itemHelper,
    SellPatch sellPatch,
    BuyPatch buyPatch,
    TraderRefreshPatch traderRefreshPatch,
    BackendConfigService backendConfigService,
    CommunityContractService communityContractService,
    ContractScheduler contractScheduler,
    WorkshopContractSyncService workshopContractSyncService,
    HttpResponseUtil httpResponseUtil
) : IOnLoad
{
    private static string _modPath = string.Empty;
    public static string ModPath => _modPath;

    public async Task OnLoad()
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

            try
            {
                await contractScheduler.TickAsync();
            }
            catch (Exception ex)
            {
                logger.DebugWarning($"[TheQuartermaster] Initial scheduler tick failed: {ex.Message}");
            }

            await communityContractService.RefreshAsync(force: true);
            communityContractService.Start();
            contractScheduler.Start();
            workshopContractSyncService.Start();
            marketplaceWorkerService.Start();

            SellPatch.SetDependencies(
                configService,
                backendConfigService,
                listingService,
                itemCloneService,
                marketplaceService,
                inventoryHelper,
                paymentService,
                questHelper,
                traderService,
                itemOverrideService,
                itemHelper,
                sellPatchLogger,
                httpResponseUtil
            );
            BuyPatch.SetDependencies(purchaseService, buyPatchLogger);
            TraderRefreshPatch.SetDependencies(traderService, communityContractService);

            sellPatch.Enable();
            buyPatch.Enable();
            traderRefreshPatch.Enable();

            logger.DebugInfo("[TheQuartermaster] Loaded successfully.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to load: {ex}", ex);
            throw;
        }
    }
}
