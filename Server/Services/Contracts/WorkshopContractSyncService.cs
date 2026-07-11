using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Cloud.Firestore;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;
using TheQuartermaster.Server.Models.Contracts;

namespace TheQuartermaster.Server.Services.Contracts;

[Injectable(InjectionType.Singleton)]
public class WorkshopContractSyncService(
    ISptLogger<WorkshopContractSyncService> logger,
    ConfigService configService,
    BackendConfigService backendConfigService,
    FirestoreContractService firestoreContractService
) : IDisposable
{
    private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private Timer? _timer;

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(configService.Config.WorkshopSyncIntervalMinutes);
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromMinutes(5);
        }

        logger.Info($"[TheQuartermaster] Starting workshop contract sync worker with interval {interval.TotalMinutes} minutes.");
        _timer = new Timer(_ => _ = Task.Run(SyncAsync), null, TimeSpan.Zero, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        logger.Info("[TheQuartermaster] Stopped workshop contract sync worker.");
    }

    public void Dispose()
    {
        Stop();
        _semaphore.Dispose();
    }

    public async Task SyncAsync()
    {
        if (!await _semaphore.WaitAsync(0))
        {
            logger.Warning("[TheQuartermaster] Workshop sync already running; skipping.");
            return;
        }

        try
        {
            var apiUrl = GetApiUrl();
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                logger.Info("[TheQuartermaster] Workshop sync disabled; no API URL configured.");
                return;
            }

            if (!firestoreContractService.IsEnabled)
            {
                logger.Warning("[TheQuartermaster] Workshop sync skipped; Firestore not available.");
                return;
            }

            logger.Info($"[TheQuartermaster] Syncing workshop contracts from {apiUrl}");

            var activeResponse = await FetchAsync(apiUrl, "active");
            var contractsResponse = await FetchAsync(apiUrl, "contracts");
            var submissionsResponse = await FetchAsync(apiUrl, "submissions");

            var definitions = new Dictionary<string, ContractDefinition>(StringComparer.OrdinalIgnoreCase);
            var schedules = new Dictionary<string, ContractScheduleEntry>(StringComparer.OrdinalIgnoreCase);
            var submissions = new Dictionary<string, ContractSubmission>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in contractsResponse.RootElement.GetProperty("items").EnumerateArray())
            {
                var (id, definition) = MapDefinition(item);
                if (definition is not null && !string.IsNullOrWhiteSpace(id))
                {
                    definitions[id] = definition;
                }
            }

            foreach (var item in activeResponse.RootElement.GetProperty("items").EnumerateArray())
            {
                var contractId = string.Empty;
                if (item.TryGetProperty("contract", out var contractElement))
                {
                    var (id, definition) = MapDefinition(contractElement);
                    if (definition is not null && !string.IsNullOrWhiteSpace(id))
                    {
                        contractId = id;
                        definitions[id] = definition;
                    }
                }

                if (item.TryGetProperty("schedule", out var scheduleElement) && !string.IsNullOrWhiteSpace(contractId))
                {
                    var scheduleId = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    var scheduleEntry = MapScheduleEntry(scheduleId, contractId, scheduleElement);
                    if (scheduleEntry is not null && !string.IsNullOrWhiteSpace(scheduleEntry.Id))
                    {
                        schedules[scheduleEntry.Id] = scheduleEntry;
                    }
                }
            }

            foreach (var item in submissionsResponse.RootElement.GetProperty("items").EnumerateArray())
            {
                var (id, submission) = MapSubmission(item);
                if (submission is not null && !string.IsNullOrWhiteSpace(id))
                {
                    submissions[id] = submission;
                }
            }

            var existingDefinitions = (await firestoreContractService.GetDefinitionsAsync())
                .ToDictionary(d => d.Id!, StringComparer.OrdinalIgnoreCase);
            var existingSchedules = (await firestoreContractService.GetScheduleEntriesAsync())
                .ToDictionary(s => s.Id!, StringComparer.OrdinalIgnoreCase);
            var existingSubmissions = (await firestoreContractService.GetAllSubmissionsAsync())
                .ToDictionary(s => s.Id!, StringComparer.OrdinalIgnoreCase);

            foreach (var (id, definition) in definitions)
            {
                if (!ShouldUpdate(existingDefinitions.GetValueOrDefault(id)?.UpdatedAt, definition.UpdatedAt))
                {
                    continue;
                }

                await firestoreContractService.UpdateDefinitionAsync(definition);
            }

            foreach (var (_, scheduleEntry) in schedules)
            {
                if (!ShouldUpdate(existingSchedules.GetValueOrDefault(scheduleEntry.Id!)?.UpdatedAt, scheduleEntry.UpdatedAt))
                {
                    continue;
                }

                await firestoreContractService.CreateScheduleEntryAsync(scheduleEntry);
            }

            foreach (var (_, submission) in submissions)
            {
                if (!ShouldUpdate(existingSubmissions.GetValueOrDefault(submission.Id!)?.UpdatedAt, submission.UpdatedAt))
                {
                    continue;
                }

                await firestoreContractService.UpdateSubmissionAsync(submission);
            }

            logger.Info($"[TheQuartermaster] Workshop sync complete. {definitions.Count} definitions, {schedules.Count} schedule entries, {submissions.Count} submissions.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Workshop sync failed: {ex.Message}", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string? GetApiUrl()
    {
        if (!backendConfigService.Config.WorkshopSyncEnabled)
        {
            return null;
        }

        var url = backendConfigService.Config.WorkshopApiUrl;
        return string.IsNullOrWhiteSpace(url) ? null : url;
    }

    private async Task<JsonDocument> FetchAsync(string apiUrl, string type)
    {
        var separator = apiUrl.Contains('?') ? "&" : "?";
        var url = $"{apiUrl}{separator}type={type}";

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private (string? id, ContractDefinition? definition) MapDefinition(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idElement) || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            return (null, null);
        }

        var id = idElement.GetString()!;
        var now = Timestamp.GetCurrentTimestamp();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["workshop_id"] = id,
            ["workshop_source"] = "workshop",
            ["workshop_synced_at"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        if (element.TryGetProperty("metadata", out var metadataElement) && metadataElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in metadataElement.EnumerateObject())
            {
                if (!metadata.ContainsKey(property.Name))
                {
                    metadata[property.Name] = property.Value.ToString() ?? string.Empty;
                }
            }
        }

        var definition = new ContractDefinition
        {
            Id = id,
            Title = GetString(element, "title") ?? string.Empty,
            Description = GetString(element, "description") ?? string.Empty,
            StartedMessage = GetString(element, "started_message") ?? string.Empty,
            SuccessMessage = GetString(element, "success_message") ?? string.Empty,
            FailMessage = GetString(element, "fail_message") ?? string.Empty,
            Status = GetString(element, "status") ?? ContractStatus.Approved,
            RecurrenceType = GetString(element, "recurrence_type") ?? ContractRecurrenceType.OneTime,
            CreatedBy = GetString(element, "created_by") ?? string.Empty,
            AuthorUid = GetString(element, "author_uid") ?? string.Empty,
            Source = GetString(element, "source") ?? "workshop",
            AdminCreated = GetBool(element, "admin_created"),
            AdminFeatured = GetBool(element, "admin_featured"),
            AdminBlocked = false,
            SptVersion = GetString(element, "spt_version") ?? backendConfigService.Config.SptVersion,
            IsNew = GetBool(element, "new"),
            Keep = GetBool(element, "keep"),
            ImageDataUrl = GetString(element, "image_data_url"),
            Objectives = MapObjectives(element),
            Rewards = MapRewards(element),
            Upvotes = GetInt(element, "upvotes") ?? 0,
            Downvotes = GetInt(element, "downvotes") ?? 0,
            ApprovalRatio = GetDouble(element, "approval_ratio"),
            CreatedAt = GetTimestamp(element, "created_at"),
            VotingEndsAt = GetTimestamp(element, "voting_ends_at"),
            ApprovedAt = GetTimestamp(element, "approved_at"),
            ScheduledStartAt = GetTimestamp(element, "scheduled_start_at"),
            ScheduledEndAt = GetTimestamp(element, "scheduled_end_at"),
            ActivatedAt = GetTimestamp(element, "activated_at"),
            ExpiredAt = GetTimestamp(element, "expired_at"),
            RejectedAt = GetTimestamp(element, "rejected_at"),
            LastUsedAt = GetTimestamp(element, "last_used_at"),
            UpdatedAt = GetTimestamp(element, "updated_at") ?? now,
            ValidationErrors = MapStringArray(element, "validation_errors"),
            Metadata = metadata
        };

        return (id, definition);
    }

    private ContractScheduleEntry? MapScheduleEntry(string? scheduleId, string contractId, JsonElement element)
    {
        var id = string.IsNullOrWhiteSpace(scheduleId) ? Guid.NewGuid().ToString("N") : scheduleId;

        var entry = new ContractScheduleEntry
        {
            Id = id,
            ContractDefinitionId = contractId,
            TemplateId = contractId,
            Status = GetString(element, "status") ?? ContractStatus.Active,
            RecurrenceType = GetString(element, "recurrence_type") ?? ContractRecurrenceType.OneTime,
            ActivationSource = "workshop",
            StartAt = GetTimestamp(element, "start_at"),
            EndAt = GetTimestamp(element, "end_at"),
            ActivatedAt = GetTimestamp(element, "activated_at"),
            ExpiresAt = GetTimestamp(element, "expires_at") ?? GetTimestamp(element, "end_at"),
            ExpiredAt = GetTimestamp(element, "expired_at"),
            AdminCreated = false,
            CreatedAt = GetTimestamp(element, "created_at") ?? Timestamp.GetCurrentTimestamp(),
            UpdatedAt = GetTimestamp(element, "updated_at") ?? Timestamp.GetCurrentTimestamp(),
            QuestId = string.IsNullOrWhiteSpace(GetString(element, "quest_id")) ? GenerateStableQuestId(id) : GetString(element, "quest_id")
        };

        return entry;
    }

    private (string? id, ContractSubmission? submission) MapSubmission(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idElement) || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            return (null, null);
        }

        var id = idElement.GetString()!;
        var submission = new ContractSubmission
        {
            Id = id,
            Title = GetString(element, "title") ?? string.Empty,
            Description = GetString(element, "description") ?? string.Empty,
            StartedMessage = GetString(element, "started_message") ?? string.Empty,
            SuccessMessage = GetString(element, "success_message") ?? string.Empty,
            FailMessage = GetString(element, "fail_message") ?? string.Empty,
            CreatedBy = GetString(element, "created_by") ?? string.Empty,
            AuthorUid = GetString(element, "author_uid") ?? string.Empty,
            Source = "workshop",
            AdminCreated = GetBool(element, "admin_created"),
            AdminFeatured = GetBool(element, "admin_featured"),
            AdminBlocked = false,
            SptVersion = GetString(element, "spt_version") ?? backendConfigService.Config.SptVersion,
            Objectives = MapObjectives(element),
            Rewards = MapRewards(element),
            DurationHours = GetPositiveInt(element, "duration_hours", 24),
            Upvotes = GetInt(element, "upvotes") ?? 0,
            Downvotes = GetInt(element, "downvotes") ?? 0,
            ApprovalRatio = GetDouble(element, "approval_ratio"),
            Status = GetString(element, "status") ?? ContractStatus.PendingVote,
            RecurrenceType = GetString(element, "recurrence_type") ?? ContractRecurrenceType.OneTime,
            SubmittedAt = GetTimestamp(element, "submitted_at"),
            VotingEndsAt = GetTimestamp(element, "voting_ends_at"),
            ApprovedAt = GetTimestamp(element, "approved_at"),
            RejectedAt = GetTimestamp(element, "rejected_at"),
            UpdatedAt = GetTimestamp(element, "updated_at") ?? Timestamp.GetCurrentTimestamp(),
            ValidationErrors = MapStringArray(element, "validation_errors")
        };

        return (id, submission);
    }

    private static List<ContractObjective> MapObjectives(JsonElement element)
    {
        if (!element.TryGetProperty("objectives", out var objectivesElement) || objectivesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<ContractObjective>();
        foreach (var obj in objectivesElement.EnumerateArray())
        {
            if (obj.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            result.Add(new ContractObjective
            {
                Type = GetString(obj, "type") ?? string.Empty,
                Description = GetString(obj, "description") ?? string.Empty,
                TargetTpl = GetString(obj, "target_tpl"),
                TargetMap = GetString(obj, "target_map"),
                TargetZone = GetString(obj, "target_zone"),
                TargetFaction = GetString(obj, "target_faction"),
                Count = GetPositiveInt(obj, "count", 1),
                RequiredInRaid = GetBool(obj, "required_in_raid")
            });
        }

        return result;
    }

    private static ContractRewards MapRewards(JsonElement element)
    {
        var rewards = new ContractRewards
        {
            Roubles = 0,
            Experience = 0,
            TraderStanding = 0.0,
            Items = []
        };

        if (!element.TryGetProperty("rewards", out var rewardsElement) || rewardsElement.ValueKind != JsonValueKind.Object)
        {
            return rewards;
        }

        rewards.Roubles = GetInt(rewardsElement, "roubles") ?? 0;
        rewards.Experience = GetInt(rewardsElement, "experience") ?? 0;
        rewards.TraderStanding = GetDouble(rewardsElement, "trader_standing");

        if (rewardsElement.TryGetProperty("money", out var moneyElement) && moneyElement.ValueKind == JsonValueKind.Object)
        {
            rewards.Money = new MoneyReward
            {
                Currency = GetString(moneyElement, "currency") ?? "RUB",
                Amount = GetInt(moneyElement, "amount") ?? 0
            };
        }

        if (rewardsElement.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var tpl = GetString(item, "tpl") ?? GetString(item, "_tpl") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(tpl))
                {
                    continue;
                }

                rewards.Items.Add(new RewardItem
                {
                    Tpl = tpl,
                    Count = GetPositiveInt(item, "count", 1),
                    FoundInRaid = GetBool(item, "found_in_raid") || GetBool(item, "foundInRaid")
                });
            }
        }

        return rewards;
    }

    private static List<string> MapStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var arrayElement) || arrayElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                result.Add(item.GetString() ?? string.Empty);
            }
        }

        return result;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.GetString();
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False)
        {
            return false;
        }

        return property.GetBoolean();
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return property.GetInt32();
    }

    private static int GetPositiveInt(JsonElement element, string propertyName, int fallback)
    {
        var value = GetInt(element, propertyName);
        return value is > 0 ? value.Value : fallback;
    }

    private static double GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return 0.0;
        }

        return property.GetDouble();
    }

    private static Timestamp? GetTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Object
            && property.TryGetProperty("_seconds", out var secondsElement)
            && secondsElement.TryGetInt64(out var seconds))
        {
            var nanoseconds = property.TryGetProperty("_nanoseconds", out var nanosecondsElement) && nanosecondsElement.TryGetInt32(out var nsValue) ? nsValue : 0;
            return Timestamp.FromDateTime(DateTime.UnixEpoch.AddSeconds(seconds).AddTicks(nanoseconds / 100).ToUniversalTime());
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return Timestamp.FromDateTime(DateTime.UnixEpoch.AddMilliseconds(property.GetInt64()).ToUniversalTime());
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            return Timestamp.FromDateTime(dt.ToUniversalTime());
        }

        return null;
    }

    private static string GenerateQuestId()
    {
        var bytes = new byte[12];
        Random.Shared.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateStableQuestId(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    private static bool ShouldUpdate(Timestamp? existingUpdatedAt, Timestamp? incomingUpdatedAt)
    {
        if (!existingUpdatedAt.HasValue)
        {
            return true;
        }

        if (!incomingUpdatedAt.HasValue)
        {
            return false;
        }

        return incomingUpdatedAt.Value.ToDateTime() > existingUpdatedAt.Value.ToDateTime();
    }
}
