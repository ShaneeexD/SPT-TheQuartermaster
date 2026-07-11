using Google.Cloud.Firestore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        if (!backendConfigService.Config.CommunityContractsEnabled || !backendConfigService.Config.AllowAutoScheduling || !firestoreContractService.IsEnabled)
        {
            return;
        }

        await MigrateEmptyQuestIdsAsync();
        await ExpireActiveEntriesAsync();
        await ActivateScheduledEntriesAsync();
        await FillEmptySlotsAsync();
    }

    private async Task MigrateEmptyQuestIdsAsync()
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
            if (string.IsNullOrWhiteSpace(entry.TemplateId) && !string.IsNullOrWhiteSpace(entry.ContractDefinitionId))
            {
                entry.TemplateId = entry.ContractDefinitionId;
            }

            await firestoreContractService.UpdateScheduleEntryAsync(entry);
            logger.Info($"[TheQuartermaster] Assigned quest ID {entry.QuestId} to schedule entry {entry.Id}.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string GenerateQuestId()
    {
        var bytes = new byte[12];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ComputeContentHash(ContractDefinition definition)
    {
        var normalized = new
        {
            definition.Title,
            definition.Description,
            Objectives = definition.Objectives
                .OrderBy(o => o.Type)
                .ThenBy(o => o.Description)
                .ThenBy(o => o.TargetTpl)
                .ThenBy(o => o.TargetMap)
                .ThenBy(o => o.TargetZone)
                .ThenBy(o => o.TargetFaction)
                .ThenBy(o => o.Count)
                .ThenBy(o => o.RequiredInRaid)
                .Select(o => new
                {
                    o.Type,
                    o.Description,
                    o.TargetTpl,
                    o.TargetMap,
                    o.TargetZone,
                    o.TargetFaction,
                    o.Count,
                    o.RequiredInRaid
                })
                .ToList(),
            Rewards = new
            {
                definition.Rewards?.Roubles,
                definition.Rewards?.Experience,
                definition.Rewards?.TraderStanding,
                Items = (definition.Rewards?.Items ?? [])
                    .OrderBy(i => i.Tpl)
                    .ThenBy(i => i.Count)
                    .ThenBy(i => i.FoundInRaid)
                    .Select(i => new { i.Tpl, i.Count, i.FoundInRaid })
                    .ToList()
            }
        };

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task ExpireActiveEntriesAsync()
    {
        var toExpire = await firestoreContractService.GetActiveEntriesToExpireAsync();
        var now = Timestamp.GetCurrentTimestamp();
        foreach (var entry in toExpire)
        {
            entry.Status = ContractStatus.Expired;
            entry.ExpiresAt = now;
            entry.ExpiredAt = now;
            await firestoreContractService.UpdateScheduleEntryAsync(entry);

            if (!string.IsNullOrWhiteSpace(entry.ContractDefinitionId))
            {
                var definition = await GetDefinitionForUpdateAsync(entry.ContractDefinitionId);
                if (definition is not null)
                {
                    definition.ExpiredAt = now;
                    await firestoreContractService.UpdateDefinitionAsync(definition);
                }
            }

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
        var activeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [ContractRecurrenceType.Daily] = activeEntries.Count(e => e.RecurrenceType == ContractRecurrenceType.Daily),
            [ContractRecurrenceType.Weekly] = activeEntries.Count(e => e.RecurrenceType == ContractRecurrenceType.Weekly),
            [ContractRecurrenceType.Weekend] = activeEntries.Count(e => e.RecurrenceType == ContractRecurrenceType.Weekend),
            [ContractRecurrenceType.OneTime] = activeEntries.Count(e => e.RecurrenceType == ContractRecurrenceType.OneTime)
        };

        var activeDefinitionIds = new HashSet<string>(activeEntries.Select(e => e.ContractDefinitionId), StringComparer.OrdinalIgnoreCase);
        var definitionsById = (await firestoreContractService.GetDefinitionsAsync())
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .ToDictionary(d => d.Id!, StringComparer.OrdinalIgnoreCase);
        var activeContentHashes = new HashSet<string>(
            activeEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.ContractDefinitionId))
                .Select(e => definitionsById.TryGetValue(e.ContractDefinitionId, out var def) ? def : null)
                .Where(def => def is not null)
                .Select(def => ComputeContentHash(def!))
                .Distinct(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var maxSlots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [ContractRecurrenceType.Daily] = backendConfigService.Config.MaxActiveDailyContracts,
            [ContractRecurrenceType.Weekly] = backendConfigService.Config.MaxActiveWeeklyContracts,
            [ContractRecurrenceType.Weekend] = backendConfigService.Config.MaxActiveSpecialContracts,
            [ContractRecurrenceType.OneTime] = backendConfigService.Config.MaxActiveSpecialContracts
        };

        var now = Timestamp.GetCurrentTimestamp();
        foreach (var entry in scheduled.OrderBy(e => e.StartAt?.ToDateTime() ?? DateTime.MinValue))
        {
            if (!maxSlots.TryGetValue(entry.RecurrenceType, out var max) || activeCounts[entry.RecurrenceType] >= max)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.ContractDefinitionId) && activeDefinitionIds.Contains(entry.ContractDefinitionId))
            {
                logger.Warning($"[TheQuartermaster] Skipping activation of {entry.Id}; an active entry for definition {entry.ContractDefinitionId} already exists.");
                continue;
            }

            ContractDefinition? candidateDef = null;
            if (!string.IsNullOrWhiteSpace(entry.ContractDefinitionId)
                && definitionsById.TryGetValue(entry.ContractDefinitionId, out candidateDef)
                && activeContentHashes.Contains(ComputeContentHash(candidateDef)))
            {
                logger.Warning($"[TheQuartermaster] Skipping activation of {entry.Id}; an active entry with the same quest content already exists.");
                continue;
            }

            entry.Status = ContractStatus.Active;
            entry.ActivatedAt = now;
            entry.ExpiresAt ??= entry.EndAt;
            if (string.IsNullOrWhiteSpace(entry.TemplateId) && !string.IsNullOrWhiteSpace(entry.ContractDefinitionId))
            {
                entry.TemplateId = entry.ContractDefinitionId;
            }

            await firestoreContractService.UpdateScheduleEntryAsync(entry);

            if (!string.IsNullOrWhiteSpace(entry.ContractDefinitionId) && candidateDef is not null)
            {
                var definition = await GetDefinitionForUpdateAsync(entry.ContractDefinitionId);
                if (definition is not null)
                {
                    definition.ActivatedAt = now;
                    await firestoreContractService.UpdateDefinitionAsync(definition);
                }

                activeDefinitionIds.Add(entry.ContractDefinitionId);
                activeContentHashes.Add(ComputeContentHash(candidateDef));
            }

            activeCounts[entry.RecurrenceType]++;
            logger.Info($"[TheQuartermaster] Activated {entry.RecurrenceType} contract schedule entry: {entry.Id}.");
        }
    }

    private async Task FillEmptySlotsAsync()
    {
        var activeEntries = await firestoreContractService.GetActiveScheduleEntriesAsync();
        var activeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [ContractRecurrenceType.Daily] = activeEntries.Count(e => e.RecurrenceType == ContractRecurrenceType.Daily),
            [ContractRecurrenceType.Weekly] = activeEntries.Count(e => e.RecurrenceType == ContractRecurrenceType.Weekly),
            [ContractRecurrenceType.Weekend] = activeEntries.Count(e => e.RecurrenceType == ContractRecurrenceType.Weekend)
        };

        var activeDefinitionIds = new HashSet<string>(activeEntries.Select(e => e.ContractDefinitionId), StringComparer.OrdinalIgnoreCase);
        var definitionsById = (await firestoreContractService.GetDefinitionsAsync())
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .ToDictionary(d => d.Id!, StringComparer.OrdinalIgnoreCase);

        var activeContentHashes = new HashSet<string>(
            activeEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.ContractDefinitionId))
                .Select(e => definitionsById.TryGetValue(e.ContractDefinitionId, out var def) ? def : null)
                .Where(def => def is not null)
                .Select(def => ComputeContentHash(def!))
                .Distinct(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var maxSlots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [ContractRecurrenceType.Daily] = backendConfigService.Config.MaxActiveDailyContracts,
            [ContractRecurrenceType.Weekly] = backendConfigService.Config.MaxActiveWeeklyContracts,
            [ContractRecurrenceType.Weekend] = backendConfigService.Config.MaxActiveSpecialContracts
        };

        var durations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [ContractRecurrenceType.Daily] = backendConfigService.Config.DailyContractDurationHours,
            [ContractRecurrenceType.Weekly] = backendConfigService.Config.WeeklyContractDurationHours,
            [ContractRecurrenceType.Weekend] = backendConfigService.Config.WeeklyContractDurationHours
        };

        var templates = await firestoreContractService.GetApprovedTemplatesAsync();
        if (templates.Count == 0)
        {
            return;
        }

        var templateHashes = templates.ToDictionary(t => t, ComputeContentHash);
        var now = DateTime.UtcNow;
        var nowTs = Timestamp.FromDateTime(now.ToUniversalTime());
        var cooldown = TimeSpan.FromDays(backendConfigService.Config.CommunityContractCooldownDays);
        var rng = new Random();

        foreach (var recurrence in maxSlots.Keys)
        {
            if (string.Equals(recurrence, ContractRecurrenceType.OneTime, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            while (activeCounts[recurrence] < maxSlots[recurrence])
            {
                var pool = templates
                    .Where(t => string.Equals(t.RecurrenceType, recurrence, StringComparison.OrdinalIgnoreCase))
                    .Where(t => !activeDefinitionIds.Contains(t.Id!))
                    .Where(t => !activeContentHashes.Contains(templateHashes[t]))
                    .Where(t => backendConfigService.Config.AllowRepeatTemplates || !t.LastUsedAt.HasValue || t.LastUsedAt.Value.ToDateTime().Add(cooldown) <= now)
                    .ToList();

                if (pool.Count == 0)
                {
                    if (backendConfigService.Config.AllowRepeatTemplates && activeDefinitionIds.Count < templates.Count)
                    {
                        pool = templates
                            .Where(t => string.Equals(t.RecurrenceType, recurrence, StringComparison.OrdinalIgnoreCase))
                            .Where(t => !activeDefinitionIds.Contains(t.Id!))
                            .Where(t => !activeContentHashes.Contains(templateHashes[t]))
                            .ToList();
                    }

                    if (pool.Count == 0)
                    {
                        logger.Warning($"[TheQuartermaster] No available templates for {recurrence} slot (cooldown active, none of matching recurrence, all matching definitions active, or content already active in another slot).");
                        break;
                    }
                }

                var template = pool[rng.Next(pool.Count)];
                var end = now + TimeSpan.FromHours(durations[recurrence]);
                var entry = new ContractScheduleEntry
                {
                    ContractDefinitionId = template.Id!,
                    TemplateId = template.Id!,
                    Status = ContractStatus.Active,
                    RecurrenceType = recurrence,
                    ActivationSource = "automatic",
                    StartAt = nowTs,
                    EndAt = Timestamp.FromDateTime(end.ToUniversalTime()),
                    ActivatedAt = nowTs,
                    ExpiresAt = Timestamp.FromDateTime(end.ToUniversalTime()),
                    AdminCreated = template.AdminCreated,
                    CreatedAt = nowTs,
                    QuestId = GenerateQuestId()
                };

                var created = await firestoreContractService.CreateScheduleEntryAtomicAsync(entry, template, recurrence, maxSlots[recurrence]);
                if (created is null)
                {
                    break;
                }

                activeDefinitionIds.Add(template.Id!);
                activeContentHashes.Add(templateHashes[template]);
                activeCounts[recurrence]++;
                logger.Info($"[TheQuartermaster] Filled {recurrence} slot with template {template.Id}.");
            }
        }
    }

    private async Task<ContractDefinition?> GetDefinitionForUpdateAsync(string definitionId)
    {
        var definitions = await firestoreContractService.GetDefinitionsAsync();
        return definitions.FirstOrDefault(d => string.Equals(d.Id, definitionId, StringComparison.OrdinalIgnoreCase));
    }
}
