using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;
using TheQuartermaster.Server.Models.Contracts;
using TheQuartermaster.Server.Services;

namespace TheQuartermaster.Server.Services.Contracts;

[Injectable(InjectionType.Singleton)]
public class CommunityContractService(
    ISptLogger<CommunityContractService> logger,
    ConfigService configService,
    BackendConfigService backendConfigService,
    FirestoreContractService firestoreContractService,
    ContractInjectionService contractInjectionService
) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private Timer? _timer;
    private DateTime _lastRefresh = DateTime.MinValue;
    private long _cachedVersion = -1;
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(1);

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(configService.Config.CommunityContractIntervalMinutes);
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromMinutes(15);
        }

        logger.DebugInfo($"[TheQuartermaster] Starting community contract worker with interval {interval.TotalMinutes} minutes.");
        _timer = new Timer(_ => _ = Task.Run(TickAsync), null, TimeSpan.Zero, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        logger.DebugInfo("[TheQuartermaster] Stopped community contract worker.");
    }

    public void Dispose()
    {
        Stop();
        _semaphore.Dispose();
    }

    public async Task RefreshAsync(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastRefresh < MinRefreshInterval)
        {
            return;
        }

        await TickAsync();
    }

    public async Task TickAsync()
    {
        if (!await _semaphore.WaitAsync(0))
        {
            logger.DebugWarning("[TheQuartermaster] Community contract tick already running; skipping.");
            return;
        }

        try
        {
            _lastRefresh = DateTime.UtcNow;
            logger.DebugInfo("[TheQuartermaster] Community contract tick started.");

            if (!firestoreContractService.IsEnabled)
            {
                logger.DebugWarning("[TheQuartermaster] Community contracts disabled (Firestore not available).");
                return;
            }

            if (!backendConfigService.Config.CommunityContractsEnabled)
            {
                logger.DebugInfo("[TheQuartermaster] Community contracts disabled by backend config.");
                return;
            }

            var version = await firestoreContractService.GetContractVersionAsync();
            if (version == _cachedVersion)
            {
                logger.DebugDebug($"[TheQuartermaster] Community contract version {version} unchanged; skipping refresh.");
                return;
            }

            var activeEntries = await firestoreContractService.GetActiveScheduleEntriesAsync();
            var definitionIds = activeEntries.Select(e => e.ContractDefinitionId).Distinct().ToList();

            var definitions = (await firestoreContractService.GetDefinitionsAsync(
                    ContractStatus.Approved,
                    ContractStatus.AdminFeatured
                ))
                .Where(d => !string.IsNullOrWhiteSpace(d.Id) && definitionIds.Contains(d.Id))
                .Where(d => configService.Config.AllowCommunityContracts || d.AdminCreated || d.AdminFeatured)
                .Where(d => configService.Config.AllowAdminContracts || (!d.AdminCreated && !d.AdminFeatured))
                .ToDictionary(d => d.Id!, StringComparer.OrdinalIgnoreCase);

            await contractInjectionService.InjectActiveContractsAsync(activeEntries, definitions);
            _cachedVersion = version;

            logger.Info($"[TheQuartermaster] Community contract tick complete. {activeEntries.Count} active schedule entr(y/ies).");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Community contract tick failed: {ex.Message}", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
