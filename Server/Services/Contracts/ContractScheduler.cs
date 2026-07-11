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
    ConfigService configService,
    BackendConfigService backendConfigService,
    FirestoreContractService firestoreContractService
) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private Timer? _timer;

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(configService.Config.ContractSchedulerIntervalMinutes);
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromMinutes(5);
        }

        logger.Info($"[TheQuartermaster] Starting contract scheduler worker with interval {interval.TotalMinutes} minutes.");
        _timer = new Timer(_ => _ = Task.Run(TickAsync), null, TimeSpan.Zero, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        logger.Info("[TheQuartermaster] Stopped contract scheduler worker.");
    }

    public void Dispose()
    {
        Stop();
        _semaphore.Dispose();
    }

    public async Task TickAsync()
    {
        if (!await _semaphore.WaitAsync(0))
        {
            logger.Warning("[TheQuartermaster] Contract scheduler tick already running; skipping.");
            return;
        }

        try
        {
            if (!backendConfigService.Config.CommunityContractsEnabled || !backendConfigService.Config.AllowAutoScheduling || !firestoreContractService.IsEnabled)
            {
                return;
            }

            await MigrateEmptyQuestIdsAsync();
            await ExpireActiveEntriesAsync();
            await DeleteExpiredDefinitionsAsync();
            await ActivateScheduledEntriesAsync();
            await FillEmptySlotsAsync();
        }
        finally
        {
            _semaphore.Release();
        }
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
                Money = new
                {
                    definition.Rewards?.Money?.Currency,
                    definition.Rewards?.Money?.Amount
                },
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

    private async Task DeleteExpiredDefinitionsAsync()
    {
        var expiredEntries = await firestoreContractService.GetScheduleEntriesAsync(ContractStatus.Expired);
        var definitions = (await firestoreContractService.GetDefinitionsAsync())
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .ToDictionary(d => d.Id!, StringComparer.OrdinalIgnoreCase);

        var cutoff = DateTime.UtcNow.AddDays(-7);
        foreach (var entry in expiredEntries)
        {
            var isOneTime = string.Equals(entry.RecurrenceType, ContractRecurrenceType.OneTime, StringComparison.OrdinalIgnoreCase);
            if (entry.ExpiredAt?.ToDateTime() > cutoff && !isOneTime)
            {
                continue;
            }

            if (!isOneTime
                && !string.IsNullOrWhiteSpace(entry.ContractDefinitionId)
                && definitions.TryGetValue(entry.ContractDefinitionId, out var definition)
                && definition.Keep)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.ContractDefinitionId))
            {
                await firestoreContractService.DeleteDefinitionAsync(entry.ContractDefinitionId);
            }

            if (!string.IsNullOrWhiteSpace(entry.Id))
            {
                await firestoreContractService.DeleteScheduleEntryAsync(entry.Id);
            }
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
                    definition.IsNew = false;
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
            [ContractRecurrenceType.Weekend] = backendConfigService.Config.MaxActiveSpecialContracts,
            [ContractRecurrenceType.OneTime] = backendConfigService.Config.MaxActiveSpecialContracts
        };

        var templates = await firestoreContractService.GetApprovedTemplatesAsync();
        if (templates.Count == 0)
        {
            return;
        }

        var templateHashes = templates.ToDictionary(t => t, ComputeContentHash);
        var now = DateTime.UtcNow;
        var cooldown = TimeSpan.FromDays(backendConfigService.Config.CommunityContractCooldownDays);
        var rng = new Random();

        var allScheduleEntries = await firestoreContractService.GetScheduleEntriesAsync();

        foreach (var recurrence in maxSlots.Keys)
        {
            if (activeCounts[recurrence] >= maxSlots[recurrence])
            {
                continue;
            }

            DateTime windowStartUtc;
            DateTime windowEndUtc;
            if (string.Equals(recurrence, ContractRecurrenceType.OneTime, StringComparison.OrdinalIgnoreCase))
            {
                windowStartUtc = now;
                windowEndUtc = now.AddDays(1);
            }
            else
            {
                (windowStartUtc, windowEndUtc) = GetRecurrenceWindow(recurrence, now);
            }
            var existingForWindow = allScheduleEntries.Any(e =>
                string.Equals(e.RecurrenceType, recurrence, StringComparison.OrdinalIgnoreCase) &&
                e.StartAt?.ToDateTime() >= windowStartUtc.AddMinutes(-1) &&
                e.StartAt?.ToDateTime() <= windowEndUtc.AddMinutes(1));

            if (existingForWindow)
            {
                logger.Debug($"[TheQuartermaster] {recurrence} window already has a schedule entry; skipping.");
                continue;
            }

            var basePool = templates
                .Where(t => string.Equals(t.RecurrenceType, recurrence, StringComparison.OrdinalIgnoreCase))
                .Where(t => !activeDefinitionIds.Contains(t.Id!))
                .Where(t => !activeContentHashes.Contains(templateHashes[t]))
                .Where(t => backendConfigService.Config.AllowRepeatTemplates || !t.LastUsedAt.HasValue || t.LastUsedAt.Value.ToDateTime().Add(cooldown) <= now)
                .ToList();

            var newPool = basePool.Where(t => t.IsNew).ToList();
            var pool = newPool.Count > 0 ? newPool : basePool;

            if (pool.Count == 0)
            {
                if (backendConfigService.Config.AllowRepeatTemplates && activeDefinitionIds.Count < templates.Count)
                {
                    var fallbackPool = templates
                        .Where(t => string.Equals(t.RecurrenceType, recurrence, StringComparison.OrdinalIgnoreCase))
                        .Where(t => !activeDefinitionIds.Contains(t.Id!))
                        .Where(t => !activeContentHashes.Contains(templateHashes[t]))
                        .ToList();

                    var fallbackNewPool = fallbackPool.Where(t => t.IsNew).ToList();
                    pool = fallbackNewPool.Count > 0 ? fallbackNewPool : fallbackPool;
                }

                if (pool.Count == 0)
                {
                    logger.Warning($"[TheQuartermaster] No available templates for {recurrence} slot (cooldown active, none of matching recurrence, all matching definitions active, or content already active in another slot).");
                    continue;
                }
            }

            var template = pool[rng.Next(pool.Count)];
            template.IsNew = false;
            var startTs = Timestamp.FromDateTime(windowStartUtc.ToUniversalTime());
            var endTs = Timestamp.FromDateTime(windowEndUtc.ToUniversalTime());
            var entry = new ContractScheduleEntry
            {
                ContractDefinitionId = template.Id!,
                TemplateId = template.Id!,
                Status = ContractStatus.Scheduled,
                RecurrenceType = recurrence,
                ActivationSource = "automatic",
                StartAt = startTs,
                EndAt = endTs,
                ActivatedAt = startTs,
                ExpiresAt = endTs,
                AdminCreated = template.AdminCreated,
                CreatedAt = Timestamp.FromDateTime(now.ToUniversalTime()),
                QuestId = GenerateQuestId()
            };

            var created = await firestoreContractService.CreateScheduleEntryAsync(entry, template, recurrence, maxSlots[recurrence]);
            if (created is null)
            {
                continue;
            }

            activeDefinitionIds.Add(template.Id!);
            activeContentHashes.Add(templateHashes[template]);
            activeCounts[recurrence]++;
            logger.Info($"[TheQuartermaster] Filled {recurrence} slot with template {template.Id} for window {windowStartUtc:O} to {windowEndUtc:O}.");
        }
    }

    private static (DateTime StartUtc, DateTime EndUtc) GetRecurrenceWindow(string recurrenceType, DateTime utcNow)
    {
        var tz = GetLondonTimeZone();
        var londonNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);

        DateTime start;
        DateTime end;
        if (string.Equals(recurrenceType, ContractRecurrenceType.Daily, StringComparison.OrdinalIgnoreCase))
        {
            start = new DateTime(londonNow.Year, londonNow.Month, londonNow.Day, 0, 0, 0, DateTimeKind.Unspecified);
            end = start.AddDays(1);
        }
        else if (string.Equals(recurrenceType, ContractRecurrenceType.Weekly, StringComparison.OrdinalIgnoreCase))
        {
            var daysSinceMonday = ((int)londonNow.DayOfWeek + 6) % 7;
            start = new DateTime(londonNow.Year, londonNow.Month, londonNow.Day, 0, 1, 0, DateTimeKind.Unspecified).AddDays(-daysSinceMonday);
            end = start.AddDays(4).AddMinutes(-1);
            if (londonNow >= end)
            {
                start = start.AddDays(7);
                end = end.AddDays(7);
            }
        }
        else if (string.Equals(recurrenceType, ContractRecurrenceType.Weekend, StringComparison.OrdinalIgnoreCase))
        {
            var daysSinceFriday = ((int)londonNow.DayOfWeek + 2) % 7;
            start = new DateTime(londonNow.Year, londonNow.Month, londonNow.Day, 17, 0, 0, DateTimeKind.Unspecified).AddDays(-daysSinceFriday);
            end = start.AddDays(3).AddHours(7).AddMinutes(1);
            if (londonNow >= end)
            {
                start = start.AddDays(7);
                end = end.AddDays(7);
                }
        }
        else
        {
            start = londonNow;
            end = start.AddDays(1);
        }

        return (TimeZoneInfo.ConvertTimeToUtc(start, tz), TimeZoneInfo.ConvertTimeToUtc(end, tz));
    }

    private static TimeZoneInfo GetLondonTimeZone()
    {
        foreach (var id in new[] { "GMT Standard Time", "Europe/London", "Europe/Belfast" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // try next id
            }
        }

        return TimeZoneInfo.Utc;
    }

    private async Task<ContractDefinition?> GetDefinitionForUpdateAsync(string definitionId)
    {
        var definitions = await firestoreContractService.GetDefinitionsAsync();
        return definitions.FirstOrDefault(d => string.Equals(d.Id, definitionId, StringComparison.OrdinalIgnoreCase));
    }
}
