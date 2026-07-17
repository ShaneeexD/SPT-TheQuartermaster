using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class ScavengedItemService(
    ISptLogger<ScavengedItemService> logger,
    RealtimeDatabaseService realtimeDatabaseService
)
{
    public async Task<ScavengedItem?> SaveScavengedItemAsync(ScavengedItem item)
    {
        if (!realtimeDatabaseService.IsEnabled)
        {
            logger.DebugWarning("[TheQuartermaster] RTDB unavailable; cannot save scavenged item.");
            return null;
        }

        try
        {
            return await realtimeDatabaseService.SaveScavengedItemAsync(item);
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to save scavenged item: {ex.Message}", ex);
            return null;
        }
    }
}
