using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class MarketplaceWorkerService(
    ISptLogger<MarketplaceWorkerService> logger,
    ConfigService configService,
    MarketplaceService marketplaceService,
    RealtimeDatabaseService realtimeDatabaseService,
    ItemOverrideService itemOverrideService
)
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private Timer? _timer;
    private DateTime _lastTick = DateTime.MinValue;

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(configService.Config.WorkerIntervalMinutes);
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromMinutes(60);
        }

        logger.DebugInfo($"[TheQuartermaster] Starting marketplace worker with interval {interval.TotalMinutes} minutes.");
        _timer = new Timer(_ => _ = Task.Run(TickAsync), null, TimeSpan.Zero, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        logger.DebugInfo("[TheQuartermaster] Stopped marketplace worker.");
    }

    public async Task TickAsync()
    {
        if (!configService.Config.ModEnabled)
        {
            return;
        }

        if (!marketplaceService.IsEnabled)
        {
            return;
        }

        if (!await _semaphore.WaitAsync(0))
        {
            logger.DebugWarning("[TheQuartermaster] Marketplace worker tick already running; skipping.");
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            _lastTick = now;

            if (!await realtimeDatabaseService.TryAcquireCatalogueLeaseAsync(TimeSpan.FromMinutes(2)))
            {
                logger.DebugDebug("[TheQuartermaster] Marketplace worker could not acquire RTDB lease; skipping.");
                return;
            }

            logger.DebugDebug("[TheQuartermaster] Marketplace worker tick started.");

            await itemOverrideService.RefreshAsync();
            await marketplaceService.CleanupExpiredListingsAsync();
            await marketplaceService.DeleteExpiredListingsAsync();
            await marketplaceService.CleanupSoldListingsAsync();

            await realtimeDatabaseService.ReleaseCatalogueLeaseAsync();

            logger.DebugDebug("[TheQuartermaster] Marketplace worker tick complete.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Marketplace worker tick failed: {ex.Message}", ex);
            await realtimeDatabaseService.ReleaseCatalogueLeaseAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
