using Google.Cloud.Firestore;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;
using TheQuartermaster.Server.Models.Contracts;
using TheQuartermaster.Server.Services;

namespace TheQuartermaster.Server.Services.Contracts;

[Injectable(InjectionType.Singleton)]
public class ContractVotingService(
    ISptLogger<ContractVotingService> logger,
    BackendConfigService backendConfigService,
    FirestoreContractService firestoreContractService,
    ContractValidationService contractValidationService
)
{
    public async Task ProcessPendingSubmissionsAsync()
    {
        if (!backendConfigService.Config.CommunityContractsEnabled || !firestoreContractService.IsEnabled)
        {
            return;
        }

        var submissions = await firestoreContractService.GetPendingSubmissionsAsync();
        if (submissions.Count == 0)
        {
            return;
        }

        logger.DebugInfo($"[TheQuartermaster] Processing {submissions.Count} pending contract submission(s).");

        foreach (var submission in submissions)
        {
            await ProcessSubmissionAsync(submission);
        }
    }

    private async Task ProcessSubmissionAsync(ContractSubmission submission)
    {
        if (submission.AdminBlocked)
        {
            submission.Status = ContractStatus.Rejected;
            submission.ValidationErrors = ["Blocked by admin."];
            await firestoreContractService.UpdateSubmissionAsync(submission);
            logger.DebugInfo($"[TheQuartermaster] Submission {submission.Id} rejected (admin block).");
            return;
        }

        var validation = contractValidationService.Validate(submission);
        if (!validation.IsValid)
        {
            submission.Status = ContractStatus.Rejected;
            submission.ValidationErrors = validation.Errors;
            await firestoreContractService.UpdateSubmissionAsync(submission);
            logger.DebugInfo($"[TheQuartermaster] Submission {submission.Id} rejected (validation failed).");
            return;
        }

        if (submission.AdminCreated)
        {
            var definition = await firestoreContractService.CreateDefinitionFromSubmissionAsync(submission);
            if (definition is not null)
            {
                definition.Status = submission.AdminFeatured ? ContractStatus.AdminFeatured : ContractStatus.Approved;
                definition.AdminCreated = true;
                definition.AdminFeatured = submission.AdminFeatured;
                await firestoreContractService.UpdateDefinitionAsync(definition);
            }

            submission.Status = ContractStatus.Approved;
            await firestoreContractService.UpdateSubmissionAsync(submission);
            logger.DebugInfo($"[TheQuartermaster] Admin submission {submission.Id} approved automatically.");
            return;
        }

        var now = DateTime.UtcNow;
        var submittedAt = submission.SubmittedAt?.ToDateTime() ?? now;
        var baseDeadline = submittedAt.AddHours(backendConfigService.Config.VotingHours);

        // Use the stored deadline only if it has been extended by the mod past the base deadline.
        var effectiveDeadline = submission.VotingEndsAt.HasValue && submission.VotingEndsAt.Value.ToDateTime() > baseDeadline
            ? submission.VotingEndsAt.Value.ToDateTime()
            : baseDeadline;

        if (now < effectiveDeadline)
        {
            // Voting still open
            return;
        }

        var totalVotes = submission.Upvotes + submission.Downvotes;
        var minVotes = backendConfigService.Config.MinimumVotes;
        var approvalPct = backendConfigService.Config.ApprovalPercentage;
        var maxVotingDuration = TimeSpan.FromHours(48);
        var elapsed = now - submittedAt;

        if (totalVotes >= minVotes && submission.ApprovalRatio >= approvalPct)
        {
            var definition = await firestoreContractService.CreateDefinitionFromSubmissionAsync(submission);
            if (definition is not null)
            {
                definition.Status = ContractStatus.Approved;
                await firestoreContractService.UpdateDefinitionAsync(definition);
            }

            submission.Status = ContractStatus.Approved;
            await firestoreContractService.UpdateSubmissionAsync(submission);
            logger.DebugInfo($"[TheQuartermaster] Community contract approved: {submission.Title} (ratio {submission.ApprovalRatio:F1}%, {totalVotes} votes).");
        }
        else if (totalVotes < minVotes && submission.ApprovalRatio > approvalPct && elapsed < maxVotingDuration)
        {
            var extension = TimeSpan.FromHours(6);
            var newDeadline = now + extension;
            var maxDeadline = submittedAt + maxVotingDuration;

            if (newDeadline > maxDeadline)
            {
                newDeadline = maxDeadline;
            }

            submission.VotingEndsAt = Timestamp.FromDateTime(newDeadline.ToUniversalTime());
            await firestoreContractService.UpdateSubmissionAsync(submission);
            logger.DebugInfo($"[TheQuartermaster] Voting extended for {submission.Title} until {newDeadline:O} ({totalVotes}/{minVotes} votes, {submission.ApprovalRatio:F1}% approval).");
        }
        else
        {
            submission.Status = ContractStatus.Rejected;
            submission.ValidationErrors =
            [
                $"Voting ended with {totalVotes} votes and {submission.ApprovalRatio:F1}% approval (required {minVotes} votes and {approvalPct}%)."
            ];
            await firestoreContractService.UpdateSubmissionAsync(submission);
            logger.DebugInfo($"[TheQuartermaster] Community contract rejected: {submission.Title} (ratio {submission.ApprovalRatio:F1}%, {totalVotes} votes).");
        }
    }
}
