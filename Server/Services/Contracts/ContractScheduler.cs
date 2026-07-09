using Google.Cloud.Firestore;
using System.Security.Cryptography;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;
using TheQuartermaster.Server.Models.Contracts;

namespace TheQuartermaster.Server.Services.Contracts;

[Injectable(InjectionType.Singleton)]
public class ContractScheduler(
    ISptLogger<ContractScheduler> logger,
    BackendConfigService backendConfigService,
    FirestoreContractService firestoreContractService
)
{
    public async Task TickAsync()
    {
        await backendConfigService.RefreshAsync();
        if (!backendConfigService.Config.CommunityContractsEnabled || !backendConfigService.Config.AllowAutoScheduling || !firestoreContractService.IsEnabled)
        {
            return;
        }

        await EnsureQuestIdsAsync();
        await ExpireActiveEntriesAsync();
        await ActivateScheduledEntriesAsync();
    }

    private async Task EnsureQuestIdsAsync()
    {
        var active = await firestoreContractService.GetActiveScheduleEntriesAsync();
        var scheduled = await firestoreContractService.GetScheduledEntriesToActivateAsync();
        var entries = active.Concat(scheduled).ToList();

        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.QuestId))
            {
                continue;
            }

            entry.QuestId = GenerateQuestId();
            await firestoreContractService.UpdateScheduleEntryAsync(entry);
            logger.Info($"[TheQuartermaster] Assigned quest ID {entry.QuestId} to schedule entry {entry.Id}.");
        }
    }

    private static string GenerateQuestId()
    {
        var bytes = new byte[12];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task ExpireActiveEntriesAsync()
    {
        var toExpire = await firestoreContractService.GetActiveEntriesToExpireAsync();
        foreach (var entry in toExpire)
        {
            entry.Status = ContractStatus.Expired;
            entry.ExpiresAt = Timestamp.GetCurrentTimestamp();
            await firestoreContractService.UpdateScheduleEntryAsync(entry);
            logger.Info($"[TheQuartermaster] Contract schedule entry expired: {entry.Id}.");
        }
    }

    private async Task ActivateScheduledEntriesAsync()
    {
        var scheduled = await firestoreContractService.GetScheduledEntriesToActivateAsync();
        if (scheduled.Count == 0)
        {
            return;
        }

        var activeEntries = await firestoreContractService.GetActiveScheduleEntriesAsync();
        var activeDaily = activeEntries.Count(e => e.RecurrenceType == ContractRecurrenceType.Daily);
        var activeWeekly = activeEntries.Count(e => e.RecurrenceType == ContractRecurrenceType.Weekly);

        var maxDaily = backendConfigService.Config.MaxActiveDailyContracts;
        var maxWeekly = backendConfigService.Config.MaxActiveWeeklyContracts;

        foreach (var entry in scheduled.OrderBy(e => e.StartAt?.ToDateTime() ?? DateTime.MinValue))
        {
            if (entry.RecurrenceType == ContractRecurrenceType.Daily && activeDaily >= maxDaily)
            {
                continue;
            }

            if (entry.RecurrenceType == ContractRecurrenceType.Weekly && activeWeekly >= maxWeekly)
            {
                continue;
            }

            entry.Status = ContractStatus.Active;
            entry.ActivatedAt = Timestamp.GetCurrentTimestamp();
            await firestoreContractService.UpdateScheduleEntryAsync(entry);

            if (entry.RecurrenceType == ContractRecurrenceType.Daily)
            {
                activeDaily++;
            }
            else if (entry.RecurrenceType == ContractRecurrenceType.Weekly)
            {
                activeWeekly++;
            }

            logger.Info($"[TheQuartermaster] Activated {entry.RecurrenceType} contract schedule entry: {entry.Id}.");
        }
    }
}
