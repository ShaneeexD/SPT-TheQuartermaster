using SPTarkov.DI.Annotations;
using SPTarkov.Common.Models.Logging;

namespace TheQuartermaster.Server.Services;

/// <summary>
/// Sends a periodic presence heartbeat to RTDB so the Oracle VM cron can
/// track current/peak player counts across all mod instances. Mirrors
/// MarketplaceWorkerService's timer pattern.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PresenceHeartbeatService(
    ISptLogger<PresenceHeartbeatService> logger,
    ConfigService configService,
    RealtimeDatabaseService realtimeDatabaseService
)
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);
    private Timer? _timer;

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        logger.DebugInfo($"[TheQuartermaster] Starting presence heartbeat with interval {HeartbeatInterval.TotalMinutes} minutes.");
        _timer = new Timer(_ => _ = Task.Run(TickAsync), null, TimeSpan.Zero, HeartbeatInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        logger.DebugInfo("[TheQuartermaster] Stopped presence heartbeat.");
    }

    private async Task TickAsync()
    {
        if (!configService.Config.ModEnabled)
        {
            return;
        }

        await realtimeDatabaseService.SendHeartbeatAsync();
    }
}
