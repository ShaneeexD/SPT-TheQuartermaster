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

    public async Task<List<ContractVote>> GetVotesForContractAsync(string contractId)
    {
        var result = new List<ContractVote>();
        if (Db is null || string.IsNullOrWhiteSpace(contractId))
        {
            return result;
        }

        try
        {
            var snapshot = await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractVotes)
                .WhereEqualTo("contract_id", contractId)
                .GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                var vote = doc.ConvertTo<ContractVote>();
                vote.Id = doc.Id;
                result.Add(vote);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch votes for {contractId}: {ex.Message}", ex);
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
            var definition = new ContractDefinition
            {
                Title = submission.Title,
                Description = submission.Description,
                Status = submission.Status == ContractStatus.AdminFeatured ? ContractStatus.AdminFeatured : ContractStatus.Approved,
                CreatedBy = submission.CreatedBy,
                AdminCreated = false,
                AdminFeatured = false,
                SptVersion = submission.SptVersion,
                Objectives = submission.Objectives,
                Rewards = submission.Rewards,
                Upvotes = submission.Upvotes,
                Downvotes = submission.Downvotes,
                ApprovalRatio = submission.ApprovalRatio,
                CreatedAt = submission.SubmittedAt,
                VotingEndsAt = submission.VotingEndsAt,
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

    public async Task<ContractPlayerProgress?> GetPlayerProgressAsync(string scheduleEntryId, string profileIdHash)
    {
        if (Db is null || string.IsNullOrWhiteSpace(scheduleEntryId) || string.IsNullOrWhiteSpace(profileIdHash))
        {
            return null;
        }

        try
        {
            var snapshot = await Db.Collection(QuartermasterConstants.FirestoreCollections.ContractPlayerProgress)
                .WhereEqualTo("schedule_entry_id", scheduleEntryId)
                .WhereEqualTo("profile_id_hash", profileIdHash)
                .Limit(1)
                .GetSnapshotAsync();

            if (snapshot.Count == 0)
            {
                return null;
            }

            var doc = snapshot.Documents.First();
            var progress = doc.ConvertTo<ContractPlayerProgress>();
            progress.Id = doc.Id;
            return progress;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to fetch player progress for {scheduleEntryId}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<ContractPlayerProgress?> SetPlayerProgressAsync(ContractPlayerProgress progress)
    {
        if (Db is null)
        {
            return null;
        }

        try
        {
            var collection = Db.Collection(QuartermasterConstants.FirestoreCollections.ContractPlayerProgress);
            DocumentReference docRef;
            if (string.IsNullOrWhiteSpace(progress.Id))
            {
                docRef = collection.Document();
                progress.Id = docRef.Id;
            }
            else
            {
                docRef = collection.Document(progress.Id);
            }

            await docRef.SetAsync(progress, SetOptions.MergeAll);
            return progress;
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to set player progress for {progress.ScheduleEntryId}: {ex.Message}", ex);
            return null;
        }
    }
}
