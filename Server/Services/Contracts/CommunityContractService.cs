using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;
using TheQuartermaster.Server.Models.Contracts;

namespace TheQuartermaster.Server.Services.Contracts;

[Injectable(InjectionType.Singleton)]
public class CommunityContractService(
    ISptLogger<CommunityContractService> logger,
    ConfigService configService,
    BackendConfigService backendConfigService,
    FirestoreContractService firestoreContractService,
    ContractVotingService contractVotingService,
    ContractScheduler contractScheduler,
    ContractInjectionService contractInjectionService
)
{
    private DateTime _lastRefresh = DateTime.MinValue;
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(5);

    public async Task RefreshAsync(bool force = false)
    {
        if (!firestoreContractService.IsEnabled)
        {
            logger.Warning("[TheQuartermaster] Community contracts disabled (Firestore not available).");
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && now - _lastRefresh < MinRefreshInterval)
        {
            return;
        }

        _lastRefresh = now;
        logger.Info("[TheQuartermaster] Refreshing community contracts...");

        await backendConfigService.RefreshAsync();
        if (!backendConfigService.Config.CommunityContractsEnabled)
        {
            return;
        }
        await contractVotingService.ProcessPendingSubmissionsAsync();
        await contractScheduler.TickAsync();

        var activeEntries = await firestoreContractService.GetActiveScheduleEntriesAsync();
        var definitionIds = activeEntries.Select(e => e.ContractDefinitionId).Distinct().ToList();

        var definitions = (await firestoreContractService.GetDefinitionsAsync(
                ContractStatus.Approved,
                ContractStatus.AdminFeatured,
                ContractStatus.Active,
                ContractStatus.Scheduled
            ))
            .Where(d => !string.IsNullOrWhiteSpace(d.Id) && definitionIds.Contains(d.Id))
            .Where(d => configService.Config.AllowCommunityContracts || d.AdminCreated || d.AdminFeatured)
            .Where(d => configService.Config.AllowAdminContracts || (!d.AdminCreated && !d.AdminFeatured))
            .ToDictionary(d => d.Id!, StringComparer.OrdinalIgnoreCase);

        await contractInjectionService.InjectActiveContractsAsync(activeEntries, definitions);

        logger.Info($"[TheQuartermaster] Community contracts refreshed. {activeEntries.Count} active schedule entr(y/ies).");
    }
}
