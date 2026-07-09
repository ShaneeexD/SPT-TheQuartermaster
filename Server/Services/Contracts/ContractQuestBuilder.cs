using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using TheQuartermaster.Server.Models.Contracts;

namespace TheQuartermaster.Server.Services.Contracts;

public static class ContractQuestBuilder
{
    private static readonly Random Rng = new();

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["any"] = "Tarkov",
        ["bigmap"] = "Customs",
        ["factory4_day"] = "Factory (Day)",
        ["factory4_night"] = "Factory (Night)",
        ["Woods"] = "Woods",
        ["Shoreline"] = "Shoreline",
        ["Interchange"] = "Interchange",
        ["Lighthouse"] = "Lighthouse",
        ["Reserve"] = "Reserve",
        ["RezervBase"] = "Reserve",
        ["laboratory"] = "The Lab",
        ["TarkovStreets"] = "Streets of Tarkov",
        ["Sandbox"] = "Ground Zero",
        ["sandbox_high"] = "Ground Zero (High Level)"
    };

    private static readonly Dictionary<string, string> LocationIdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["any"] = "any",
        ["factory4_day"] = "55f2d3fd4bdc2d5f408b4567",
        ["factory4_night"] = "59fc81d786f774390775787e",
        ["bigmap"] = "56f40101d2720b2a4d8b45d6",
        ["Woods"] = "5704e3c2d2720bac5b8b4567",
        ["Shoreline"] = "5704e554d2720bac5b8b456e",
        ["Interchange"] = "5714dbc024597771384a510d",
        ["Lighthouse"] = "5704e4dad2720bb55b8b4567",
        ["laboratory"] = "5b0fc42d86f7744a585f9105",
        ["RezervBase"] = "5704e5fad2720bc05b8b4567",
        ["Reserve"] = "5704e5fad2720bc05b8b4567",
        ["TarkovStreets"] = "5714dc692459777137212e12",
        ["Sandbox"] = "653e6760052c01c1c805532f",
        ["sandbox_high"] = "653e6760052c01c1c805532f"
    };

    public static int BuildQuestFiles(
        string outputBaseDir,
        string traderId,
        List<ContractScheduleEntry> activeEntries,
        Dictionary<string, ContractDefinition> definitionsById,
        ItemHelper itemHelper
    )
    {
        if (activeEntries.Count == 0)
        {
            return 0;
        }

        var traderDir = Path.Combine(outputBaseDir, traderId);
        var questsDir = Path.Combine(traderDir, "Quests");
        var localesDir = Path.Combine(traderDir, "Locales");
        var imagesDir = Path.Combine(traderDir, "Images");

        Directory.CreateDirectory(questsDir);
        Directory.CreateDirectory(localesDir);
        Directory.CreateDirectory(imagesDir);

        var defaultIconPath = Path.Combine(imagesDir, "default_quest_icon.png");
        if (!File.Exists(defaultIconPath))
        {
            GenerateDefaultQuestIcon(defaultIconPath);
        }

        var allQuests = new JsonObject();
        var allLocales = new JsonObject
        {
            ["any Name"] = "Any location"
        };

        var count = 0;
        foreach (var entry in activeEntries)
        {
            if (!definitionsById.TryGetValue(entry.ContractDefinitionId, out var definition))
            {
                continue;
            }

            var questId = !string.IsNullOrWhiteSpace(entry.QuestId)
                ? entry.QuestId
                : DeriveQuestId(entry.Id!);
            var quest = BuildQuest(questId, definition, entry, allLocales, itemHelper);
            allQuests[questId] = quest;
            count++;
        }

        var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(questsDir, "quest_definitions.json"), allQuests.ToJsonString(jsonOpts));
        File.WriteAllText(Path.Combine(localesDir, "en.json"), allLocales.ToJsonString(jsonOpts));

        return count;
    }

    private static JsonObject BuildQuest(
        string questId,
        ContractDefinition definition,
        ContractScheduleEntry entry,
        JsonObject locales,
        ItemHelper itemHelper
    )
    {
        var questType = DetermineQuestType(definition.Objectives);
        var location = ResolveQuestLocation(definition.Objectives);

        var startConditions = new JsonArray
        {
            new JsonObject
            {
                ["compareMethod"] = ">=",
                ["conditionType"] = "Level",
                ["dynamicLocale"] = false,
                ["globalQuestCounterId"] = "",
                ["id"] = DeriveStableId($"{questId}:start:level"),
                ["index"] = 0,
                ["parentId"] = "",
                ["value"] = 1,
                ["visibilityConditions"] = new JsonArray()
            }
        };

        var finishConditions = new JsonArray();
        var conditionIndex = 0;
        for (var i = 0; i < definition.Objectives.Count; i++)
        {
            var conditions = BuildObjectiveConditions(definition.Objectives[i], i, questId, locales, itemHelper).ToList();
            foreach (var c in conditions)
            {
                finishConditions.Add(c);
            }

            conditionIndex += conditions.Count;
        }

        var successRewards = BuildRewards(definition.Rewards, questId);

        locales[$"{questId} name"] = definition.Title;
        locales[$"{questId} description"] = definition.Description;
        locales[$"{questId} successMessageText"] = $"Contract complete: {definition.Title}";
        locales[$"{questId} startedMessageText"] = $"Contract accepted: {definition.Title}";
        locales[$"{questId} acceptPlayerMessage"] = $"Contract accepted: {definition.Title}";
        locales[$"{questId} completePlayerMessage"] = $"Contract complete: {definition.Title}";

        return new JsonObject
        {
            ["QuestName"] = definition.Title,
            ["_id"] = questId,
            ["traderId"] = QuartermasterConstants.TraderId.ToString(),
            ["type"] = questType,
            ["location"] = location,
            ["image"] = "/files/quest/icon/default_quest_icon.png",
            ["instantComplete"] = false,
            ["isKey"] = false,
            ["secretQuest"] = false,
            ["restartable"] = false,
            ["canShowNotificationsInGame"] = true,
            ["name"] = $"{questId} name",
            ["description"] = $"{questId} description",
            ["note"] = "Community Contract",
            ["side"] = "Pmc",
            ["sideExclusive"] = "Pmc",
            ["startedMessageText"] = $"{questId} startedMessageText",
            ["successMessageText"] = $"{questId} successMessageText",
            ["failMessageText"] = $"{questId} failMessageText",
            ["acceptPlayerMessage"] = $"{questId} acceptPlayerMessage",
            ["declinePlayerMessage"] = $"{questId} declinePlayerMessage",
            ["completePlayerMessage"] = $"{questId} completePlayerMessage",
            ["changeQuestMessageText"] = $"{questId} changeQuestMessageText",
            ["templateId"] = "",
            ["conditions"] = new JsonObject
            {
                ["AvailableForStart"] = startConditions,
                ["AvailableForFinish"] = finishConditions,
                ["Fail"] = new JsonArray()
            },
            ["rewards"] = new JsonObject
            {
                ["Started"] = new JsonArray(),
                ["Success"] = successRewards,
                ["Fail"] = new JsonArray()
            }
        };
    }

    private static IEnumerable<JsonNode> BuildObjectiveConditions(
        ContractObjective objective,
        int index,
        string questId,
        JsonObject locales,
        ItemHelper itemHelper
    )
    {
        var condId = DeriveStableId($"{questId}:obj{index}:cond");
        var counterId = DeriveStableId($"{questId}:obj{index}:counter");

        switch (objective.Type)
        {
            case ContractObjectiveType.HandOverItem:
            case ContractObjectiveType.HandOverFirItem:
                var handover = BuildHandoverCondition(objective, condId, objective.Type == ContractObjectiveType.HandOverFirItem, itemHelper);
                locales[condId] = objective.Description;
                yield return handover;
                break;

            case ContractObjectiveType.KillScavs:
            case ContractObjectiveType.KillPmcs:
            case ContractObjectiveType.KillBoss:
                var kill = BuildKillCondition(objective, condId, counterId);
                locales[condId] = objective.Description;
                yield return kill;
                break;

            case ContractObjectiveType.SurviveMap:
            case ContractObjectiveType.ExtractMap:
                var survive = BuildSurviveCondition(objective, condId, counterId);
                locales[condId] = objective.Description;
                yield return survive;
                break;
        }
    }

    private static JsonObject BuildHandoverCondition(ContractObjective objective, string condId, bool foundInRaid, ItemHelper itemHelper)
    {
        var itemName = GetItemName(objective.TargetTpl, itemHelper);
        return new JsonObject
        {
            ["conditionType"] = "HandoverItem",
            ["dogtagLevel"] = 0,
            ["dynamicLocale"] = false,
            ["globalQuestCounterId"] = "",
            ["id"] = condId,
            ["index"] = 0,
            ["isEncoded"] = false,
            ["maxDurability"] = 100,
            ["minDurability"] = 0,
            ["onlyFoundInRaid"] = foundInRaid,
            ["parentId"] = "",
            ["target"] = new JsonArray { objective.TargetTpl },
            ["value"] = objective.Count,
            ["visibilityConditions"] = new JsonArray()
        };
    }

    private static JsonObject BuildKillCondition(ContractObjective objective, string condId, string counterId)
    {
        var (target, savageRole) = objective.Type switch
        {
            ContractObjectiveType.KillScavs => ("Savage", new JsonArray()),
            ContractObjectiveType.KillPmcs => ("AnyPmc", new JsonArray()),
            ContractObjectiveType.KillBoss => ("Savage", new JsonArray { objective.TargetFaction ?? "bossTagilla" }),
            _ => ("Savage", new JsonArray())
        };

        var killCond = new JsonObject
        {
            ["bodyPart"] = new JsonArray(),
            ["compareMethod"] = ">=",
            ["conditionType"] = "Kills",
            ["daytime"] = new JsonObject { ["from"] = 0, ["to"] = 0 },
            ["distance"] = new JsonObject { ["compareMethod"] = ">=", ["value"] = 0 },
            ["dynamicLocale"] = false,
            ["enemyEquipmentExclusive"] = new JsonArray(),
            ["enemyEquipmentInclusive"] = new JsonArray(),
            ["enemyHealthEffects"] = new JsonArray(),
            ["id"] = DeriveStableId($"{condId}:kill"),
            ["resetOnSessionEnd"] = false,
            ["savageRole"] = savageRole,
            ["target"] = target,
            ["value"] = 1,
            ["weapon"] = new JsonArray(),
            ["weaponCaliber"] = new JsonArray(),
            ["weaponModsInclusive"] = new JsonArray(),
            ["weaponModsExclusive"] = new JsonArray()
        };

        var counterConditions = new JsonArray { killCond };

        if (!string.IsNullOrWhiteSpace(objective.TargetMap))
        {
            counterConditions.Add(new JsonObject
            {
                ["conditionType"] = "Location",
                ["dynamicLocale"] = false,
                ["id"] = DeriveStableId($"{condId}:loc"),
                ["target"] = new JsonArray { ToBsgLocationTarget(objective.TargetMap) }
            });
        }

        return new JsonObject
        {
            ["completeInSeconds"] = 0,
            ["conditionType"] = "CounterCreator",
            ["counter"] = new JsonObject
            {
                ["conditions"] = counterConditions,
                ["id"] = counterId
            },
            ["doNotResetIfCounterCompleted"] = false,
            ["dynamicLocale"] = false,
            ["globalQuestCounterId"] = "",
            ["id"] = condId,
            ["index"] = 0,
            ["oneSessionOnly"] = false,
            ["parentId"] = "",
            ["type"] = "Elimination",
            ["value"] = objective.Count,
            ["visibilityConditions"] = new JsonArray()
        };
    }

    private static JsonObject BuildSurviveCondition(ContractObjective objective, string condId, string counterId)
    {
        var exitCondId = DeriveStableId($"{condId}:exit");
        var locCondId = DeriveStableId($"{condId}:loc");

        var counterConditions = new JsonArray
        {
            new JsonObject
            {
                ["conditionType"] = "ExitStatus",
                ["dynamicLocale"] = false,
                ["id"] = exitCondId,
                ["status"] = new JsonArray { "Survived", "Runner" }
            }
        };

        if (!string.IsNullOrWhiteSpace(objective.TargetMap))
        {
            counterConditions.Add(new JsonObject
            {
                ["conditionType"] = "Location",
                ["dynamicLocale"] = false,
                ["id"] = locCondId,
                ["target"] = new JsonArray { ToBsgLocationTarget(objective.TargetMap) }
            });
        }

        if (!string.IsNullOrWhiteSpace(objective.TargetZone))
        {
            counterConditions.Add(new JsonObject
            {
                ["conditionType"] = "ExitName",
                ["dynamicLocale"] = false,
                ["id"] = DeriveStableId($"{condId}:exitname"),
                ["exitName"] = objective.TargetZone
            });
        }

        return new JsonObject
        {
            ["completeInSeconds"] = 0,
            ["conditionType"] = "CounterCreator",
            ["counter"] = new JsonObject
            {
                ["conditions"] = counterConditions,
                ["id"] = counterId
            },
            ["doNotResetIfCounterCompleted"] = false,
            ["dynamicLocale"] = false,
            ["globalQuestCounterId"] = "",
            ["id"] = condId,
            ["index"] = 0,
            ["oneSessionOnly"] = false,
            ["parentId"] = "",
            ["type"] = "Exploration",
            ["value"] = objective.Count,
            ["visibilityConditions"] = new JsonArray()
        };
    }

    private static JsonArray BuildRewards(ContractRewards rewards, string questId)
    {
        var result = new JsonArray();
        var idx = 0;

        if (rewards.Experience > 0)
        {
            result.Add(new JsonObject
            {
                ["availableInGameEditions"] = new JsonArray(),
                ["id"] = GenerateId(),
                ["index"] = idx++,
                ["type"] = "Experience",
                ["value"] = rewards.Experience.ToString(),
                ["unknown"] = false
            });
        }

        if (rewards.Roubles > 0)
        {
            var moneyItemId = GenerateId();
            result.Add(new JsonObject
            {
                ["availableInGameEditions"] = new JsonArray(),
                ["findInRaid"] = true,
                ["id"] = GenerateId(),
                ["index"] = idx++,
                ["items"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["_id"] = moneyItemId,
                        ["_tpl"] = Money.ROUBLES.ToString(),
                        ["upd"] = new JsonObject { ["StackObjectsCount"] = rewards.Roubles }
                    }
                },
                ["target"] = moneyItemId,
                ["type"] = "Item",
                ["value"] = rewards.Roubles.ToString(),
                ["unknown"] = false
            });
        }

        foreach (var item in rewards.Items)
        {
            var itemRewardId = GenerateId();
            result.Add(new JsonObject
            {
                ["availableInGameEditions"] = new JsonArray(),
                ["findInRaid"] = item.FoundInRaid,
                ["id"] = GenerateId(),
                ["index"] = idx++,
                ["items"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["_id"] = itemRewardId,
                        ["_tpl"] = item.Tpl,
                        ["upd"] = new JsonObject { ["StackObjectsCount"] = item.Count }
                    }
                },
                ["target"] = itemRewardId,
                ["type"] = "Item",
                ["value"] = item.Count.ToString(),
                ["unknown"] = false
            });
        }

        if (rewards.TraderStanding > 0)
        {
            result.Add(new JsonObject
            {
                ["availableInGameEditions"] = new JsonArray(),
                ["id"] = GenerateId(),
                ["index"] = idx++,
                ["target"] = QuartermasterConstants.TraderId.ToString(),
                ["type"] = "TraderStanding",
                ["value"] = rewards.TraderStanding.ToString("F2"),
                ["unknown"] = false
            });
        }

        return result;
    }

    private static string ResolveQuestLocation(List<ContractObjective> objectives)
    {
        var map = objectives
            .Select(o => o.TargetMap)
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));

        return ToQuestLocationId(map);
    }

    private static string DetermineQuestType(List<ContractObjective> objectives)
    {
        if (objectives.Count == 0)
        {
            return "PickUp";
        }

        return objectives[0].Type switch
        {
            ContractObjectiveType.KillScavs or ContractObjectiveType.KillPmcs or ContractObjectiveType.KillBoss => "Elimination",
            ContractObjectiveType.SurviveMap or ContractObjectiveType.ExtractMap => "Exploration",
            _ => "PickUp"
        };
    }

    private static string ToQuestLocationId(string? location)
    {
        if (string.IsNullOrWhiteSpace(location) || location.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            return "any";
        }

        return LocationIdMap.GetValueOrDefault(location, location);
    }

    private static string ToBsgLocationTarget(string location)
    {
        return string.Equals(location, "Reserve", StringComparison.OrdinalIgnoreCase)
            ? "RezervBase"
            : location;
    }

    private static string GetItemName(string? tpl, ItemHelper itemHelper)
    {
        if (string.IsNullOrWhiteSpace(tpl) || !MongoId.IsValidMongoId(tpl))
        {
            return "item";
        }

        var item = itemHelper.GetItem(new MongoId(tpl));
        if (item.Key && !string.IsNullOrWhiteSpace(item.Value?.Name))
        {
            return item.Value.Name;
        }

        return tpl;
    }

    private static string DeriveQuestId(string scheduleEntryId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(scheduleEntryId));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return hex[..24];
    }

    private static string DeriveStableId(string seed)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash).ToLowerInvariant()[..24];
    }

    private static string GenerateId()
    {
        var bytes = new byte[12];
        Rng.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void GenerateDefaultQuestIcon(string path)
    {
        byte[] pngBytes =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x62, 0x00, 0x00, 0x00, 0x02,
            0x00, 0x01, 0xE5, 0x27, 0xDE, 0xFC, 0x00, 0x00,
            0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42,
            0x60, 0x82
        ];

        File.WriteAllBytes(path, pngBytes);
    }
}
