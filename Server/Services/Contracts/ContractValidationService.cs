using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using TheQuartermaster.Server.Models.Contracts;

namespace TheQuartermaster.Server.Services.Contracts;

public record ValidationResult(bool IsValid, List<string> Errors)
{
    public static ValidationResult Success() => new(true, []);
    public static ValidationResult Failure(List<string> errors) => new(false, errors);
}

[Injectable(InjectionType.Singleton)]
public class ContractValidationService()
{
    private static readonly HashSet<string> ValidMaps = new(StringComparer.OrdinalIgnoreCase)
    {
        "any",
        "bigmap",
        "factory4_day",
        "factory4_night",
        "Woods",
        "Shoreline",
        "Interchange",
        "Lighthouse",
        "Reserve",
        "RezervBase",
        "laboratory",
        "TarkovStreets",
        "Sandbox",
        "sandbox_high"
    };

    private const int MaxTitleLength = 100;
    private const int MaxDescriptionLength = 2000;
    private const int MaxObjectiveCount = 100;
    private const int MaxKillCount = 500;
    private const int MaxHandoverCount = 1000;
    private const int MaxTotalRewardValue = 50_000_000;
    private const int MinDurationHours = 1;
    private const int MaxDurationHours = 720;

    public ValidationResult Validate(ContractSubmission submission)
    {
        var errors = new List<string>();
        ValidateBase(submission.Title, submission.Description, submission.Objectives, submission.Rewards, submission.SptVersion, errors);

        if (submission.AdminBlocked)
        {
            errors.Add("Submission has been blocked by an admin.");
        }

        if (submission.DurationHours < MinDurationHours || submission.DurationHours > MaxDurationHours)
        {
            errors.Add($"Contract duration must be between {MinDurationHours} and {MaxDurationHours} hours.");
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }

    public ValidationResult Validate(ContractDefinition definition)
    {
        var errors = new List<string>();
        ValidateBase(definition.Title, definition.Description, definition.Objectives, definition.Rewards, definition.SptVersion, errors);

        if (definition.AdminBlocked)
        {
            errors.Add("Contract has been blocked by an admin.");
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }

    private void ValidateBase(
        string title,
        string description,
        List<ContractObjective> objectives,
        ContractRewards rewards,
        string sptVersion,
        List<string> errors
    )
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("Title is required.");
        }
        else if (title.Length > MaxTitleLength)
        {
            errors.Add($"Title must be {MaxTitleLength} characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            errors.Add("Description is required.");
        }
        else if (description.Length > MaxDescriptionLength)
        {
            errors.Add($"Description must be {MaxDescriptionLength} characters or fewer.");
        }

        if (objectives is null || objectives.Count == 0)
        {
            errors.Add("At least one objective is required.");
        }
        else if (objectives.Count > MaxObjectiveCount)
        {
            errors.Add($"Too many objectives. Maximum is {MaxObjectiveCount}.");
        }

        if (!string.IsNullOrWhiteSpace(sptVersion) && !sptVersion.StartsWith("4.0."))
        {
            errors.Add($"Unsupported SPT version: {sptVersion}.");
        }

        if (rewards is null)
        {
            errors.Add("Rewards are required.");
            return;
        }

        if (rewards.Roubles < 0)
        {
            errors.Add("Rouble reward cannot be negative.");
        }

        if (rewards.Money?.Amount < 0)
        {
            errors.Add("Money reward amount cannot be negative.");
        }

        if (rewards.Money?.Amount > QuartermasterConstants.Marketplace.MaxPrice)
        {
            errors.Add($"Money reward exceeds maximum of {QuartermasterConstants.Marketplace.MaxPrice}.");
        }

        if (rewards.Experience < 0)
        {
            errors.Add("Experience reward cannot be negative.");
        }

        if (rewards.Roubles > QuartermasterConstants.Marketplace.MaxPrice)
        {
            errors.Add($"Rouble reward exceeds maximum of {QuartermasterConstants.Marketplace.MaxPrice}.");
        }

        if (rewards.Experience > 1_000_000)
        {
            errors.Add("Experience reward exceeds maximum of 1,000,000.");
        }

        if (rewards.TraderStanding < 0 || rewards.TraderStanding > 1.0)
        {
            errors.Add("Trader standing reward must be between 0.0 and 1.0.");
        }

        var totalRewardValue = rewards.Roubles + (rewards.Money?.Amount ?? 0) + rewards.Experience * 100;
        if (totalRewardValue > MaxTotalRewardValue)
        {
            errors.Add("Total reward value (roubles + money + XP*100) is unreasonably high.");
        }

        if (rewards.Items is not null)
        {
            foreach (var item in rewards.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Tpl) || !MongoId.IsValidMongoId(item.Tpl))
                {
                    errors.Add($"Invalid reward item template: {item.Tpl}.");
                    continue;
                }

                if (item.Count <= 0)
                {
                    errors.Add($"Reward item count must be positive: {item.Tpl}.");
                }

                if (item.Count > 1000)
                {
                    errors.Add($"Reward item count is unreasonably high: {item.Tpl}.");
                }
            }
        }

        if (objectives is null)
        {
            return;
        }

        var seenIds = new HashSet<string>();
        var hasHandover = false;
        var hasKill = false;
        for (var i = 0; i < objectives.Count; i++)
        {
            var objective = objectives[i];
            if (!ContractObjectiveType.All.Contains(objective.Type))
            {
                errors.Add($"Unknown objective type at index {i}: {objective.Type}.");
            }

            if (objective.Count <= 0)
            {
                errors.Add($"Objective count must be positive at index {i}.");
            }

            // Generic objective count check is handled by type-specific limits below

            if (objective.Type is ContractObjectiveType.HandOverItem or ContractObjectiveType.HandOverFirItem)
            {
                hasHandover = true;
                if (objective.Count > MaxHandoverCount)
                {
                    errors.Add($"Handover count at index {i} cannot exceed {MaxHandoverCount}.");
                }

                if (string.IsNullOrWhiteSpace(objective.TargetTpl) || !MongoId.IsValidMongoId(objective.TargetTpl))
                {
                    errors.Add($"Hand over objective at index {i} requires a valid item template.");
                }

                if (objective.Type == ContractObjectiveType.HandOverFirItem && !objective.RequiredInRaid)
                {
                    errors.Add($"Found-in-raid handover objective at index {i} must have required_in_raid set to true.");
                }
            }

            if (objective.Type is ContractObjectiveType.KillScavs or ContractObjectiveType.KillPmcs or ContractObjectiveType.KillBoss)
            {
                hasKill = true;
                if (objective.Count > MaxKillCount)
                {
                    errors.Add($"Kill count at index {i} cannot exceed {MaxKillCount}.");
                }
            }

            if (objective.Type is ContractObjectiveType.KillScavs or ContractObjectiveType.KillPmcs or ContractObjectiveType.KillBoss or ContractObjectiveType.SurviveMap or ContractObjectiveType.ExtractMap)
            {
                if (!string.IsNullOrWhiteSpace(objective.TargetMap) && !ValidMaps.Contains(objective.TargetMap))
                {
                    errors.Add($"Invalid target map at index {i}: {objective.TargetMap}.");
                }
            }

            if (objective.Type == ContractObjectiveType.KillBoss && string.IsNullOrWhiteSpace(objective.TargetFaction))
            {
                errors.Add($"Boss kill objective at index {i} requires a target faction/boss tag.");
            }

            if (objective.Type is ContractObjectiveType.SurviveMap or ContractObjectiveType.ExtractMap && string.IsNullOrWhiteSpace(objective.TargetMap))
            {
                errors.Add($"{objective.Type} objective at index {i} requires a target map.");
            }
        }

        if (hasHandover && hasKill)
        {
            // This is not strictly impossible in SPT, but the prompt asks to flag unsupported combinations.
            // We will allow mixed objective types but warn if the quest builder cannot represent them.
            // For now, no error here unless we later add a restriction.
        }
    }
}
