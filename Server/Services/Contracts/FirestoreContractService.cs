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
                SptVersion = submission.SptVersion,
                Objectives = submission.Objectives,
                Rewards = submission.Rewards,
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
        var result = new List<ContractDefinition>();
        if (Db is null)
        {
            return result;
        }

        try
        {
            var snapshot = await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractDefinitions)
                .WhereIn("status", new List<string> { ContractStatus.Approved, ContractStatus.AdminFeatured })
                .WhereEqualTo("admin_blocked", false)
                .GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                var definition = doc.ConvertTo<ContractDefinition>();
                definition.Id = doc.Id;
                result.Add(definition);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch approved templates: {ex.Message}", ex);
        }

        return result;
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
            return entry;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to create schedule entry: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<ContractScheduleEntry?> CreateScheduleEntryAtomicAsync(ContractScheduleEntry entry, ContractDefinition definition, string recurrenceType, int maxSlots)
    {
        if (Db is null || string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(entry.ContractDefinitionId))
        {
            return null;
        }

        try
        {
            var scheduleCollection = Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSchedule);
            var defRef = Db.Collection(QuartermasterConstants.FirestoreCollections.ContractDefinitions).Document(definition.Id);

            var created = await Db.RunTransactionAsync(async transaction =>
            {
                var activeQuery = scheduleCollection
                    .WhereEqualTo("status", ContractStatus.Active)
                    .WhereEqualTo("recurrence_type", recurrenceType)
                    .Count();
                var activeSnapshot = await transaction.GetSnapshotAsync(activeQuery);
                if ((activeSnapshot.Count ?? 0) >= maxSlots)
                {
                    return null;
                }

                var duplicateQuery = scheduleCollection
                    .WhereEqualTo("status", ContractStatus.Active)
                    .WhereEqualTo("contract_definition_id", entry.ContractDefinitionId)
                    .Count();
                var duplicateSnapshot = await transaction.GetSnapshotAsync(duplicateQuery);
                if ((duplicateSnapshot.Count ?? 0) > 0)
                {
                    return null;
                }

                var docRef = scheduleCollection.Document();
                entry.Id = docRef.Id;
                transaction.Set(docRef, entry);

                definition.LastUsedAt = entry.StartAt;
                definition.ScheduledStartAt = entry.StartAt;
                definition.ScheduledEndAt = entry.EndAt;
                transaction.Set(defRef, definition, SetOptions.MergeAll);

                return entry;
            });

            return created;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to atomically create schedule entry for {definition.Id}: {ex.Message}", ex);
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

        try
        {
            Query query = Db.Collection(QuartermasterConstants.FirestoreCollections.ContractDefinitions);
            if (statuses.Length > 0)
            {
                query = query.WhereIn("status", statuses.ToList());
            }

            var snapshot = await query.GetSnapshotAsync();
            foreach (var doc in snapshot.Documents)
            {
                var definition = doc.ConvertTo<ContractDefinition>();
                definition.Id = doc.Id;
                result.Add(definition);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch contract definitions: {ex.Message}", ex);
        }

        return result;
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
            return definition;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to update contract definition {definition.Id}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<List<ContractScheduleEntry>> GetScheduleEntriesAsync(params string[] statuses)
    {
        var result = new List<ContractScheduleEntry>();
        if (Db is null)
        {
            return result;
        }

        try
        {
            Query query = Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSchedule);
            if (statuses.Length > 0)
            {
                query = query.WhereIn("status", statuses.ToList());
            }

            var snapshot = await query.GetSnapshotAsync();
            foreach (var doc in snapshot.Documents)
            {
                var entry = doc.ConvertTo<ContractScheduleEntry>();
                entry.Id = doc.Id;
                result.Add(entry);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch schedule entries: {ex.Message}", ex);
        }

        return result;
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
        var result = new List<ContractScheduleEntry>();
        if (Db is null)
        {
            return result;
        }

        try
        {
            var now = Timestamp.GetCurrentTimestamp();
            var snapshot = await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSchedule)
                .WhereEqualTo("status", ContractStatus.Active)
                .WhereGreaterThan("end_at", now)
                .GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                var entry = doc.ConvertTo<ContractScheduleEntry>();
                entry.Id = doc.Id;
                result.Add(entry);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch active schedule entries: {ex.Message}", ex);
        }

        return result;
    }

    public async Task<List<ContractScheduleEntry>> GetScheduledEntriesToActivateAsync()
    {
        var result = new List<ContractScheduleEntry>();
        if (Db is null)
        {
            return result;
        }

        try
        {
            var now = Timestamp.GetCurrentTimestamp();
            var snapshot = await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSchedule)
                .WhereEqualTo("status", ContractStatus.Scheduled)
                .WhereLessThanOrEqualTo("start_at", now)
                .GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                var entry = doc.ConvertTo<ContractScheduleEntry>();
                entry.Id = doc.Id;
                result.Add(entry);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch scheduled entries to activate: {ex.Message}", ex);
        }

        return result;
    }

    public async Task<List<ContractScheduleEntry>> GetActiveEntriesToExpireAsync()
    {
        var result = new List<ContractScheduleEntry>();
        if (Db is null)
        {
            return result;
        }

        try
        {
            var now = Timestamp.GetCurrentTimestamp();
            var snapshot = await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractSchedule)
                .WhereEqualTo("status", ContractStatus.Active)
                .WhereLessThanOrEqualTo("end_at", now)
                .GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                var entry = doc.ConvertTo<ContractScheduleEntry>();
                entry.Id = doc.Id;
                result.Add(entry);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch active entries to expire: {ex.Message}", ex);
        }

        return result;
    }
}
