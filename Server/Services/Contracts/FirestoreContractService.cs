using Google.Cloud.Firestore;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;
using TheQuartermaster.Server.Models.Contracts;

namespace TheQuartermaster.Server.Services.Contracts;

[Injectable(InjectionType.Singleton)]
public class FirestoreContractService(
    ISptLogger<FirestoreContractService> logger,
    FirestoreService firestoreService
)
{
    private FirestoreDb? Db => firestoreService.IsEnabled ? firestoreService.Db : null;

    public bool IsEnabled => firestoreService.IsEnabled;

    private readonly object _cacheLock = new();
    private List<ContractDefinition>? _cachedDefinitions;
    private DateTime _cachedDefinitionsAt = DateTime.MinValue;
    private List<ContractScheduleEntry>? _cachedScheduleEntries;
    private DateTime _cachedScheduleEntriesAt = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    private bool IsCacheFresh(DateTime cachedAt) => cachedAt != DateTime.MinValue && DateTime.UtcNow - cachedAt < CacheTtl;

    private void InvalidateDefinitionsCache()
    {
        lock (_cacheLock)
        {
            _cachedDefinitions = null;
            _cachedDefinitionsAt = DateTime.MinValue;
        }
    }

    private void InvalidateScheduleEntriesCache()
    {
        lock (_cacheLock)
        {
            _cachedScheduleEntries = null;
            _cachedScheduleEntriesAt = DateTime.MinValue;
        }
    }

    public async Task<long> GetContractVersionAsync()
    {
        if (Db is null)
        {
            return 0;
        }

        try
        {
            var docRef = Db.Collection(QuartermasterConstants.FirestoreCollections.Meta)
                .Document(QuartermasterConstants.FirestoreConfig.ContractVersion);
            var snapshot = await docRef.GetSnapshotAsync();
            if (!snapshot.Exists)
            {
                return 0;
            }

            return snapshot.ContainsField("version") ? snapshot.GetValue<long>("version") : 0;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to read contract version: {ex.Message}", ex);
            return 0;
        }
    }

    public async Task<long> BumpContractVersionAsync()
    {
        if (Db is null)
        {
            return 0;
        }

        try
        {
            var docRef = Db.Collection(QuartermasterConstants.FirestoreCollections.Meta)
                .Document(QuartermasterConstants.FirestoreConfig.ContractVersion);
            await docRef.SetAsync(
                new { version = FieldValue.Increment(1), updated_at = FieldValue.ServerTimestamp },
                SetOptions.MergeAll);
            var snapshot = await docRef.GetSnapshotAsync();
            return snapshot.Exists && snapshot.ContainsField("version") ? snapshot.GetValue<long>("version") : 0;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to bump contract version: {ex.Message}", ex);
            return 0;
        }
    }

    public async Task<List<ContractSubmission>> GetAllSubmissionsAsync()
    {
        var result = new List<ContractSubmission>();
        if (Db is null)
        {
            return result;
        }

        try
        {
            var snapshot = await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSubmissions)
                .GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                var submission = doc.ConvertTo<ContractSubmission>();
                submission.Id = doc.Id;
                result.Add(submission);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch all contract submissions: {ex.Message}", ex);
        }

        return result;
    }

    public async Task<List<ContractSubmission>> GetPendingSubmissionsAsync()
    {
        var result = new List<ContractSubmission>();
        if (Db is null)
        {
            return result;
        }

        try
        {
            var snapshot = await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSubmissions)
                .WhereEqualTo("status", ContractStatus.PendingVote)
                .GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                var submission = doc.ConvertTo<ContractSubmission>();
                submission.Id = doc.Id;
                result.Add(submission);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch contract submissions: {ex.Message}", ex);
        }

        return result;
    }

    public async Task<ContractSubmission?> UpdateSubmissionAsync(ContractSubmission submission)
    {
        if (Db is null || string.IsNullOrWhiteSpace(submission.Id))
        {
            return null;
        }

        try
        {
            var docRef = Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSubmissions).Document(submission.Id);
            await docRef.SetAsync(submission, SetOptions.MergeAll);
            return submission;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to update submission {submission.Id}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<ContractDefinition?> CreateDefinitionFromSubmissionAsync(ContractSubmission submission)
    {
        if (Db is null || string.IsNullOrWhiteSpace(submission.Id))
        {
            return null;
        }

        try
        {
            var now = Timestamp.GetCurrentTimestamp();
            var status = submission.AdminFeatured
                ? ContractStatus.AdminFeatured
                : ContractStatus.Approved;

            var definition = new ContractDefinition
            {
                Title = submission.Title,
                Description = submission.Description,
                Status = status,
                RecurrenceType = submission.RecurrenceType,
                CreatedBy = submission.CreatedBy,
                AuthorUid = submission.AuthorUid,
                Source = submission.AdminCreated ? "admin" : "community",
                AdminCreated = submission.AdminCreated,
                AdminFeatured = submission.AdminFeatured,
                AdminBlocked = false,
                IsNew = true,
                Keep = false,
                SptVersion = submission.SptVersion,
                Objectives = submission.Objectives,
                Rewards = submission.Rewards,
                ImageDataUrl = submission.ImageDataUrl,
                Upvotes = submission.Upvotes,
                Downvotes = submission.Downvotes,
                ApprovalRatio = submission.ApprovalRatio,
                CreatedAt = submission.SubmittedAt,
                VotingEndsAt = submission.VotingEndsAt,
                ApprovedAt = now,
                RejectedAt = null,
                Metadata = new Dictionary<string, string>
                {
                    ["source_submission_id"] = submission.Id
                }
            };

            var docRef = Db.Collection(QuartermasterConstants.FirestoreCollections.ContractDefinitions).Document();
            definition.Id = docRef.Id;
            await docRef.SetAsync(definition);
            InvalidateDefinitionsCache();
            await BumpContractVersionAsync();
            return definition;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to create contract definition from submission {submission.Id}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<List<ContractDefinition>> GetApprovedTemplatesAsync()
    {
        if (Db is null)
        {
            return new List<ContractDefinition>();
        }

        var definitions = await GetDefinitionsAsync(ContractStatus.Approved, ContractStatus.AdminFeatured);
        return definitions.Where(d => !d.AdminBlocked).ToList();
    }

    public async Task<ContractScheduleEntry?> CreateScheduleEntryAsync(ContractScheduleEntry entry)
    {
        if (Db is null)
        {
            return null;
        }

        try
        {
            var docRef = string.IsNullOrWhiteSpace(entry.Id)
                ? Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSchedule).Document()
                : Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSchedule).Document(entry.Id);
            entry.Id = docRef.Id;
            await docRef.SetAsync(entry);
            InvalidateScheduleEntriesCache();
            await BumpContractVersionAsync();
            return entry;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to create schedule entry: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<ContractScheduleEntry?> CreateScheduleEntryAsync(ContractScheduleEntry entry, ContractDefinition definition, string recurrenceType, int maxSlots)
    {
        if (Db is null || string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(entry.ContractDefinitionId))
        {
            return null;
        }

        try
        {
            var scheduleCollection = Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSchedule);
            var defRef = Db.Collection(QuartermasterConstants.FirestoreCollections.ContractDefinitions).Document(definition.Id);

            var activeQuery = scheduleCollection
                .WhereEqualTo("status", ContractStatus.Active)
                .WhereEqualTo("recurrence_type", recurrenceType)
                .Count();
            var activeSnapshot = await activeQuery.GetSnapshotAsync();
            if ((activeSnapshot.Count ?? 0) >= maxSlots)
            {
                return null;
            }

            var duplicateQuery = scheduleCollection
                .WhereEqualTo("status", ContractStatus.Active)
                .WhereEqualTo("contract_definition_id", entry.ContractDefinitionId)
                .Count();
            var duplicateSnapshot = await duplicateQuery.GetSnapshotAsync();
            if ((duplicateSnapshot.Count ?? 0) > 0)
            {
                return null;
            }

            var docRef = scheduleCollection.Document();
            entry.Id = docRef.Id;
            await docRef.SetAsync(entry);

            definition.LastUsedAt = entry.StartAt;
            definition.ScheduledStartAt = entry.StartAt;
            definition.ScheduledEndAt = entry.EndAt;
            await defRef.SetAsync(definition, SetOptions.MergeAll);

            InvalidateDefinitionsCache();
            InvalidateScheduleEntriesCache();
            await BumpContractVersionAsync();

            return entry;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to create schedule entry for {definition.Id}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<List<ContractDefinition>> GetDefinitionsAsync(params string[] statuses)
    {
        var result = new List<ContractDefinition>();
        if (Db is null)
        {
            return result;
        }

        List<ContractDefinition>? cached;
        DateTime cachedAt;
        lock (_cacheLock)
        {
            cached = _cachedDefinitions;
            cachedAt = _cachedDefinitionsAt;
        }

        if (cached is not null && IsCacheFresh(cachedAt))
        {
            return FilterDefinitions(cached, statuses);
        }

        try
        {
            var snapshot = await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractDefinitions)
                .GetSnapshotAsync();
            foreach (var doc in snapshot.Documents)
            {
                var definition = doc.ConvertTo<ContractDefinition>();
                definition.Id = doc.Id;
                result.Add(definition);
            }

            lock (_cacheLock)
            {
                _cachedDefinitions = result;
                _cachedDefinitionsAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch contract definitions: {ex.Message}", ex);
        }

        return FilterDefinitions(result, statuses);
    }

    private static List<ContractDefinition> FilterDefinitions(List<ContractDefinition> definitions, string[] statuses)
    {
        if (statuses.Length == 0)
        {
            return definitions;
        }

        var statusSet = new HashSet<string>(statuses, StringComparer.OrdinalIgnoreCase);
        return definitions
            .Where(d => !string.IsNullOrWhiteSpace(d.Status) && statusSet.Contains(d.Status))
            .ToList();
    }

    public async Task<ContractDefinition?> UpdateDefinitionAsync(ContractDefinition definition)
    {
        if (Db is null || string.IsNullOrWhiteSpace(definition.Id))
        {
            return null;
        }

        try
        {
            var docRef = Db.Collection(QuartermasterConstants.FirestoreCollections.ContractDefinitions).Document(definition.Id);
            await docRef.SetAsync(definition, SetOptions.MergeAll);
            InvalidateDefinitionsCache();
            await BumpContractVersionAsync();
            return definition;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to update contract definition {definition.Id}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<bool> DeleteDefinitionAsync(string definitionId)
    {
        if (Db is null || string.IsNullOrWhiteSpace(definitionId))
        {
            return false;
        }

        try
        {
            await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractDefinitions).Document(definitionId).DeleteAsync();
            InvalidateDefinitionsCache();
            await BumpContractVersionAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to delete contract definition {definitionId}: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> DeleteScheduleEntryAsync(string entryId)
    {
        if (Db is null || string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        try
        {
            await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSchedule).Document(entryId).DeleteAsync();
            InvalidateScheduleEntriesCache();
            await BumpContractVersionAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to delete schedule entry {entryId}: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<List<ContractScheduleEntry>> GetScheduleEntriesAsync(params string[] statuses)
    {
        var result = new List<ContractScheduleEntry>();
        if (Db is null)
        {
            return result;
        }

        List<ContractScheduleEntry>? cached;
        DateTime cachedAt;
        lock (_cacheLock)
        {
            cached = _cachedScheduleEntries;
            cachedAt = _cachedScheduleEntriesAt;
        }

        if (cached is not null && IsCacheFresh(cachedAt))
        {
            return FilterScheduleEntries(cached, statuses);
        }

        try
        {
            var snapshot = await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSchedule)
                .GetSnapshotAsync();
            foreach (var doc in snapshot.Documents)
            {
                var entry = doc.ConvertTo<ContractScheduleEntry>();
                entry.Id = doc.Id;
                result.Add(entry);
            }

            lock (_cacheLock)
            {
                _cachedScheduleEntries = result;
                _cachedScheduleEntriesAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch schedule entries: {ex.Message}", ex);
        }

        return FilterScheduleEntries(result, statuses);
    }

    private static List<ContractScheduleEntry> FilterScheduleEntries(List<ContractScheduleEntry> entries, string[] statuses)
    {
        if (statuses.Length == 0)
        {
            return entries;
        }

        var statusSet = new HashSet<string>(statuses, StringComparer.OrdinalIgnoreCase);
        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Status) && statusSet.Contains(e.Status))
            .ToList();
    }

    public async Task<ContractScheduleEntry?> UpdateScheduleEntryAsync(ContractScheduleEntry entry)
    {
        if (Db is null || string.IsNullOrWhiteSpace(entry.Id))
        {
            return null;
        }

        try
        {
            var docRef = Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSchedule).Document(entry.Id);
            await docRef.SetAsync(entry, SetOptions.MergeAll);
            InvalidateScheduleEntriesCache();
            await BumpContractVersionAsync();
            return entry;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to update schedule entry {entry.Id}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<List<ContractScheduleEntry>> GetActiveScheduleEntriesAsync()
    {
        if (Db is null)
        {
            return new List<ContractScheduleEntry>();
        }

        var now = DateTime.UtcNow;
        var all = await GetScheduleEntriesAsync();
        return all
            .Where(e => string.Equals(e.Status, ContractStatus.Active, StringComparison.OrdinalIgnoreCase))
            .Where(e => e.EndAt?.ToDateTime() > now)
            .ToList();
    }

    public async Task<List<ContractScheduleEntry>> GetScheduledEntriesToActivateAsync()
    {
        if (Db is null)
        {
            return new List<ContractScheduleEntry>();
        }

        var now = DateTime.UtcNow;
        var all = await GetScheduleEntriesAsync();
        return all
            .Where(e => string.Equals(e.Status, ContractStatus.Scheduled, StringComparison.OrdinalIgnoreCase))
            .Where(e => e.StartAt?.ToDateTime() <= now)
            .ToList();
    }

    public async Task<List<ContractScheduleEntry>> GetActiveEntriesToExpireAsync()
    {
        if (Db is null)
        {
            return new List<ContractScheduleEntry>();
        }

        var now = DateTime.UtcNow;
        var all = await GetScheduleEntriesAsync();
        return all
            .Where(e => string.Equals(e.Status, ContractStatus.Active, StringComparison.OrdinalIgnoreCase))
            .Where(e => e.EndAt?.ToDateTime() <= now)
            .ToList();
    }
}
