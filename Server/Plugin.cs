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
    public override Version Version { get; init; } = new("1.0.0");
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
            logger.Info("[TheQuartermaster] Initialising...");

            configService.Load(_modPath);
            vanillaAllowlistService.Load(_modPath);
            await firestoreService.InitialiseAsync();
            await marketplaceService.InitialiseAsync();
            await backendConfigService.LoadAsync();
            await traderService.RegisterTrader(_modPath);

            await communityContractService.RefreshAsync();
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
                sellPatchLogger,
                httpResponseUtil
            );
            BuyPatch.SetDependencies(purchaseService, buyPatchLogger);
            TraderRefreshPatch.SetDependencies(traderService, communityContractService);

            sellPatch.Enable();
            buyPatch.Enable();
            traderRefreshPatch.Enable();

            logger.Info("[TheQuartermaster] Loaded successfully.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to load: {ex}", ex);
            throw;
        }
    }
}
