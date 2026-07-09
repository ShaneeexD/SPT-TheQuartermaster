using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using TheQuartermaster.Server.Models.Contracts;

namespace TheQuartermaster.Server.Services.Contracts;

public record ValidationResult(bool IsValid, List<string> Errors)
{
    public static ValidationResult Success() => new(true, []);
    public static ValidationResult Failure(List<string> errors) => new(false, errors);
}

[Injectable(InjectionType.Singleton)]
public class ContractValidationService(
    ItemHelper itemHelper
)
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

    public ValidationResult Validate(ContractSubmission submission)
    {
        var errors = new List<string>();
        ValidateBase(submission.Title, submission.Description, submission.Objectives, submission.Rewards, submission.SptVersion, errors);

        if (submission.AdminBlocked)
        {
            errors.Add("Submission has been blocked by an admin.");
        }

        if (submission.DurationHours <= 0)
        {
            errors.Add("Contract duration must be greater than zero.");
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

        if (string.IsNullOrWhiteSpace(description))
        {
            errors.Add("Description is required.");
        }

        if (objectives is null || objectives.Count == 0)
        {
            errors.Add("At least one objective is required.");
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

        if (rewards.Items is not null)
        {
            foreach (var item in rewards.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Tpl) || !MongoId.IsValidMongoId(item.Tpl))
                {
                    errors.Add($"Invalid reward item template: {item.Tpl}.");
                    continue;
                }

                if (!itemHelper.IsItemInDb(new MongoId(item.Tpl)))
                {
                    errors.Add($"Reward item template not in runtime database: {item.Tpl}.");
                }

                if (item.Count <= 0)
                {
                    errors.Add($"Reward item count must be positive: {item.Tpl}.");
                }
            }
        }

        if (objectives is null)
        {
            return;
        }

        var seenIds = new HashSet<string>();
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

            if (objective.Count > 1000)
            {
                errors.Add($"Objective count is unreasonably high at index {i}.");
            }

            if (!string.IsNullOrWhiteSpace(objective.Id) && !seenIds.Add(objective.Id))
            {
                errors.Add($"Duplicate objective id: {objective.Id}.");
            }

            if (objective.Type is ContractObjectiveType.HandOverItem or ContractObjectiveType.HandOverFirItem)
            {
                if (string.IsNullOrWhiteSpace(objective.TargetTpl) || !MongoId.IsValidMongoId(objective.TargetTpl))
                {
                    errors.Add($"Hand over objective at index {i} requires a valid item template.");
                }
                else if (!itemHelper.IsItemInDb(new MongoId(objective.TargetTpl)))
                {
                    errors.Add($"Hand over objective item template not in runtime database: {objective.TargetTpl}.");
                }

                if (objective.Type == ContractObjectiveType.HandOverFirItem && !objective.RequiredInRaid)
                {
                    // Ensure FIR is represented correctly
                    errors.Add($"Found-in-raid handover objective at index {i} must have required_in_raid set to true.");
                }
            }

            if (objective.Type is ContractObjectiveType.KillScavs or ContractObjectiveType.KillPmcs or ContractObjectiveType.KillBoss or ContractObjectiveType.SurviveMap or ContractObjectiveType.ExtractMap)
            {
                if (!string.IsNullOrWhiteSpace(objective.TargetMap) && !ValidMaps.Contains(objective.TargetMap))
                {
                    errors.Add($"Invalid target map at index {i}: {objective.TargetMap}.");
                }
            }
        }
    }
}
