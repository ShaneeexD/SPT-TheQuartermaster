using System.IO;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Eft.Ws;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
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
    ContractInjectionService contractInjectionService,
    NotificationSendHelper notificationSendHelper,
    SaveServer saveServer,
    TimeUtil timeUtil
) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private Timer? _timer;
    private DateTime _lastRefresh = DateTime.MinValue;
    private long _cachedVersion = -1;
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(1);
    private string _versionFilePath = string.Empty;
    private string _notifiedFilePath = string.Empty;
    private HashSet<string> _notifiedEntryIds = [];

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _versionFilePath = Path.Combine(configService.ModPath, "cache", "contract_version.txt");
        _notifiedFilePath = Path.Combine(configService.ModPath, "cache", "notified_contracts.json");
        _cachedVersion = LoadPersistedVersion();
        _notifiedEntryIds = LoadNotifiedEntryIds();

        var interval = TimeSpan.FromMinutes(configService.Config.CommunityContractIntervalMinutes);
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromMinutes(5);
        }

        logger.DebugInfo($"[TheQuartermaster] Starting community contract worker with interval {interval.TotalMinutes} minutes. Last known contract version: {_cachedVersion}.");
        _timer = new Timer(_ => _ = Task.Run(TickAsync), null, TimeSpan.Zero, interval);
    }

    private long LoadPersistedVersion()
    {
        try
        {
            if (File.Exists(_versionFilePath) && long.TryParse(File.ReadAllText(_versionFilePath).Trim(), out var version))
            {
                return version;
            }
        }
        catch (Exception ex)
        {
            logger.DebugWarning($"[TheQuartermaster] Failed to load persisted contract version: {ex.Message}");
        }
        return -1;
    }

    private void PersistVersion(long version)
    {
        try
        {
            var dir = Path.GetDirectoryName(_versionFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(_versionFilePath, version.ToString());
        }
        catch (Exception ex)
        {
            logger.DebugWarning($"[TheQuartermaster] Failed to persist contract version: {ex.Message}");
        }
    }

    private HashSet<string> LoadNotifiedEntryIds()
    {
        try
        {
            if (File.Exists(_notifiedFilePath))
            {
                var json = File.ReadAllText(_notifiedFilePath);
                var ids = JsonSerializer.Deserialize<List<string>>(json);
                return ids is not null ? new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase) : [];
            }
        }
        catch (Exception ex)
        {
            logger.DebugWarning($"[TheQuartermaster] Failed to load notified contract IDs: {ex.Message}");
        }
        return [];
    }

    private void PersistNotifiedEntryIds()
    {
        try
        {
            var dir = Path.GetDirectoryName(_notifiedFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(_notifiedFilePath, JsonSerializer.Serialize(_notifiedEntryIds.ToList()));
        }
        catch (Exception ex)
        {
            logger.DebugWarning($"[TheQuartermaster] Failed to persist notified contract IDs: {ex.Message}");
        }
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
            PersistVersion(version);

            if (activeEntries.Count > 0)
            {
                var newEntries = activeEntries.Where(e => !string.IsNullOrWhiteSpace(e.Id) && !_notifiedEntryIds.Contains(e.Id)).ToList();
                if (newEntries.Count > 0)
                {
                    NotifyPlayersOfNewContracts(newEntries, definitions);
                    foreach (var entry in newEntries)
                    {
                        _notifiedEntryIds.Add(entry.Id!);
                    }
                    PersistNotifiedEntryIds();
                }
                else
                {
                    logger.DebugInfo("[TheQuartermaster] No new contract entries to notify about; all already notified.");
                }
            }

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

    private void NotifyPlayersOfNewContracts(
        List<ContractScheduleEntry> activeEntries,
        Dictionary<string, ContractDefinition> definitionsById
    )
    {
        try
        {
            var newContractNames = new List<string>();
            foreach (var entry in activeEntries)
            {
                if (definitionsById.TryGetValue(entry.ContractDefinitionId, out var def))
                {
                    var prefix = entry.RecurrenceType switch
                    {
                        "daily" => "[DAILY] ",
                        "weekly" => "[WEEKLY] ",
                        "weekend" => "[WEEKEND] ",
                        "one_time" => "[ONE TIME] ",
                        _ => ""
                    };
                    newContractNames.Add($"{prefix}{def.Title}");
                }
            }

            if (newContractNames.Count == 0)
            {
                return;
            }

            var messageText = newContractNames.Count == 1
                ? $"New community contract available: {newContractNames[0]}"
                : $"New community contracts available:\n{string.Join("\n", newContractNames.Select(n => $"• {n}"))}";

            var traderId = new MongoId(QuartermasterConstants.TraderId.ToString());
            var profiles = saveServer.GetProfiles();

            foreach (var (sessionId, _) in profiles)
            {
                try
                {
                    var message = new Message
                    {
                        Id = new MongoId(),
                        UserId = traderId,
                        MessageType = MessageType.NpcTraderMessage,
                        DateTime = timeUtil.GetTimeStamp(),
                        Text = messageText,
                        HasRewards = null,
                        RewardCollected = null,
                        Items = null,
                    };

                    var dialogueData = saveServer.GetProfile(sessionId).DialogueRecords;
                    if (dialogueData is not null)
                    {
                        if (dialogueData.TryGetValue(traderId, out var dialog))
                        {
                            dialog.New += 1;
                            dialog.Messages?.Add(message);
                        }
                        else
                        {
                            dialogueData[traderId] = new Dialogue
                            {
                                Id = traderId,
                                Type = MessageType.NpcTraderMessage,
                                Messages = [message],
                                Pinned = false,
                                New = 1,
                                AttachmentsNew = 0,
                                Users = null,
                            };
                        }
                    }

                    var notification = new WsChatMessageReceived
                    {
                        EventType = NotificationEventType.new_message,
                        EventIdentifier = message.Id,
                        DialogId = traderId,
                        Message = message,
                    };

                    notificationSendHelper.SendMessage(sessionId, notification);
                }
                catch (Exception ex)
                {
                    logger.DebugWarning($"[TheQuartermaster] Failed to send contract notification to session {sessionId}: {ex.Message}");
                }
            }

            logger.DebugInfo($"[TheQuartermaster] Sent contract notification to {profiles.Count} profile(s).");
        }
        catch (Exception ex)
        {
            logger.DebugWarning($"[TheQuartermaster] Failed to send contract notifications: {ex.Message}");
        }
    }
}
