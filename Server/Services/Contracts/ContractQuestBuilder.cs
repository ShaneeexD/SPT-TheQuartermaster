using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        Dictionary<string, ContractDefinition> definitionsById
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

        var modPath = Path.GetFullPath(Path.Combine(outputBaseDir, "..", ".."));
        var sourceQuestIcon = Path.Combine(modPath, "Assets", "quest.png");
        var defaultIconPath = Path.Combine(imagesDir, "quest.png");
        if (File.Exists(sourceQuestIcon))
        {
            File.Copy(sourceQuestIcon, defaultIconPath, true);
        }
        else if (!File.Exists(defaultIconPath))
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
                : GenerateQuestId();
            var quest = BuildQuest(questId, definition, entry, allLocales);
            quest["image"] = "/files/quest/icon/quest.png";
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
        JsonObject locales
    )
    {
        var questType = DetermineQuestType(definition.Objectives);
        var location = ResolveQuestLocation(definition.Objectives);
        var recurrencePrefix = GetRecurrencePrefix(entry.RecurrenceType);
        var questTitle = $"{recurrencePrefix}{definition.Title}";

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
            var conditions = BuildObjectiveConditions(definition.Objectives[i], i, questId, locales).ToList();
            foreach (var c in conditions)
            {
                finishConditions.Add(c);
            }

            conditionIndex += conditions.Count;
        }

        var successRewards = BuildRewards(definition.Rewards, questId);

        var startedMessage = string.IsNullOrWhiteSpace(definition.StartedMessage)
            ? $"Contract accepted: {questTitle}"
            : definition.StartedMessage;
        var successMessage = string.IsNullOrWhiteSpace(definition.SuccessMessage)
            ? $"Contract complete: {questTitle}"
            : definition.SuccessMessage;
        var failMessage = string.IsNullOrWhiteSpace(definition.FailMessage)
            ? string.Empty
            : definition.FailMessage;

        locales[$"{questId} name"] = questTitle;
        var description = definition.Description;
        if (entry.ExpiresAt is { } expiresAt)
        {
            var expiryUtc = expiresAt.ToDateTime();
            var remaining = expiryUtc - DateTimeOffset.UtcNow;
            if (remaining.TotalSeconds > 0)
            {
                var expiryIso = expiryUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var hours = (int)remaining.TotalHours;
                var minutes = remaining.Minutes;
                var timeLabel = hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
                description += $"\n\n[QM_EXPIRY:{expiryIso}]\nExpires in {timeLabel}";
            }
        }
        locales[$"{questId} description"] = description;
        locales[$"{questId} successMessageText"] = successMessage;
        locales[$"{questId} startedMessageText"] = startedMessage;
        locales[$"{questId} failMessageText"] = failMessage;
        locales[$"{questId} acceptPlayerMessage"] = startedMessage;
        locales[$"{questId} completePlayerMessage"] = successMessage;

        return new JsonObject
        {
            ["QuestName"] = questTitle,
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
        JsonObject locales
    )
    {
        var condId = DeriveStableId($"{questId}:obj{index}:cond");
        var counterId = DeriveStableId($"{questId}:obj{index}:counter");
        var dynamicLocale = string.IsNullOrWhiteSpace(objective.Description);
        if (!string.IsNullOrWhiteSpace(objective.Description))
        {
            locales[condId] = objective.Description;
        }

        switch (objective.Type)
        {
            case ContractObjectiveType.HandOverItem:
            case ContractObjectiveType.HandOverFirItem:
                var handover = BuildHandoverCondition(objective, condId, objective.Type == ContractObjectiveType.HandOverFirItem, dynamicLocale);
                yield return handover;
                break;

            case ContractObjectiveType.KillScavs:
            case ContractObjectiveType.KillPmcs:
            case ContractObjectiveType.KillBoss:
                var kill = BuildKillCondition(objective, condId, counterId, dynamicLocale);
                yield return kill;
                break;

            case ContractObjectiveType.SurviveMap:
            case ContractObjectiveType.ExtractMap:
                var survive = BuildSurviveCondition(objective, condId, counterId, dynamicLocale);
                yield return survive;
                break;
        }
    }


    private static string ToDisplayName(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return "Tarkov";
        }

        return DisplayNames.TryGetValue(location, out var display) ? display : location;
    }

    private static string ToBossDisplayName(string? faction)
    {
        if (string.IsNullOrWhiteSpace(faction))
        {
            return "boss";
        }

        return faction.ToLowerInvariant() switch
        {
            "bossbully" => "Reshala",
            "bosskilla" => "Killa",
            "bosskojaniy" => "Shturman",
            "bosssanitar" => "Sanitar",
            "bosstagilla" => "Tagilla",
            "bossgluhar" => "Gluhar",
            "bosszryachiy" => "Zryachiy",
            "bossboar" => "Kaban",
            "bosspartisan" => "Partisan",
            "bosskolontay" => "Kolontay",
            "bossknight" => "Knight",
            "sectantpriest" => "Cultist Priest",
            "sectantwarrior" => "Cultist Warrior",
            _ => faction
        };
    }

    private static JsonObject BuildHandoverCondition(ContractObjective objective, string condId, bool foundInRaid, bool dynamicLocale)
    {
        return new JsonObject
        {
            ["conditionType"] = "HandoverItem",
            ["dogtagLevel"] = 0,
            ["dynamicLocale"] = dynamicLocale,
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

    private static JsonObject BuildKillCondition(ContractObjective objective, string condId, string counterId, bool dynamicLocale)
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
            ["dynamicLocale"] = dynamicLocale,
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
                ["dynamicLocale"] = dynamicLocale,
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
            ["dynamicLocale"] = dynamicLocale,
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

    private static JsonObject BuildSurviveCondition(ContractObjective objective, string condId, string counterId, bool dynamicLocale)
    {
        var exitCondId = DeriveStableId($"{condId}:exit");
        var locCondId = DeriveStableId($"{condId}:loc");

        var counterConditions = new JsonArray
        {
            new JsonObject
            {
                ["conditionType"] = "ExitStatus",
                ["dynamicLocale"] = dynamicLocale,
                ["id"] = exitCondId,
                ["status"] = new JsonArray { "Survived", "Runner" }
            }
        };

        if (!string.IsNullOrWhiteSpace(objective.TargetMap))
        {
            counterConditions.Add(new JsonObject
            {
                ["conditionType"] = "Location",
                ["dynamicLocale"] = dynamicLocale,
                ["id"] = locCondId,
                ["target"] = new JsonArray { ToBsgLocationTarget(objective.TargetMap) }
            });
        }

        if (!string.IsNullOrWhiteSpace(objective.TargetZone))
        {
            counterConditions.Add(new JsonObject
            {
                ["conditionType"] = "ExitName",
                ["dynamicLocale"] = dynamicLocale,
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
            ["dynamicLocale"] = dynamicLocale,
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

        var moneyReward = rewards.Money is { Amount: > 0 }
            ? rewards.Money
            : (rewards.Roubles > 0 ? new MoneyReward { Currency = "RUB", Amount = rewards.Roubles } : null);

        if (moneyReward is not null)
        {
            var moneyTpl = ResolveMoneyTemplateId(moneyReward.Currency);
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
                        ["_tpl"] = moneyTpl,
                        ["upd"] = new JsonObject { ["StackObjectsCount"] = moneyReward.Amount }
                    }
                },
                ["target"] = moneyItemId,
                ["type"] = "Item",
                ["value"] = moneyReward.Amount.ToString(),
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


    internal static string GenerateQuestId()
    {
        var bytes = new byte[12];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
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

    private static string GetRecurrencePrefix(string recurrenceType)
    {
        if (string.Equals(recurrenceType, ContractRecurrenceType.Daily, StringComparison.OrdinalIgnoreCase))
        {
            return "[DAILY] ";
        }
        if (string.Equals(recurrenceType, ContractRecurrenceType.Weekly, StringComparison.OrdinalIgnoreCase))
        {
            return "[WEEKLY] ";
        }
        if (string.Equals(recurrenceType, ContractRecurrenceType.Weekend, StringComparison.OrdinalIgnoreCase))
        {
            return "[WEEKEND] ";
        }
        if (string.Equals(recurrenceType, ContractRecurrenceType.OneTime, StringComparison.OrdinalIgnoreCase))
        {
            return "[ONE TIME] ";
        }
        return string.Empty;
    }

    private static string ResolveMoneyTemplateId(string currency)
    {
        var upper = currency?.ToUpperInvariant() ?? "RUB";
        return upper switch
        {
            "USD" or "DOLLARS" or "DOLLAR" => "5696686a4bdc2da3298b456a",
            "EUR" or "EUROS" or "EURO" => "569668774bdc2da2298b4568",
            _ => Money.ROUBLES.ToString()
        };
    }

    private static void GenerateDefaultQuestIcon(string path)
    {
        // 64x64 opaque blue placeholder so it isn't a blank 1x1 pixel.
        File.WriteAllBytes(path, CreateSolidPng(64, 64, 0x00, 0x80, 0xFF, 0xFF));
    }

    private static byte[] CreateSolidPng(int width, int height, byte r, byte g, byte b, byte a)
    {
        var output = new MemoryStream();

        // PNG signature.
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        output.Write(signature);

        // IHDR
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // colour type RGBA
        ihdr[10] = 0;  // compression
        ihdr[11] = 0;  // filter
        ihdr[12] = 0;  // interlace
        WritePngChunk(output, "IHDR", ihdr);

        // Raw image data: one filter byte per row then RGBA pixels.
        var rowSize = 1 + width * 4;
        var raw = new byte[height * rowSize];
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * rowSize;
            raw[rowStart] = 0; // filter: None
            for (var x = 0; x < width; x++)
            {
                var idx = rowStart + 1 + x * 4;
                raw[idx] = r;
                raw[idx + 1] = g;
                raw[idx + 2] = b;
                raw[idx + 3] = a;
            }
        }

        // IDAT (zlib compressed)
        using var idat = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionLevel.Optimal, true))
        {
            zlib.Write(raw);
        }
        WritePngChunk(output, "IDAT", idat.ToArray());

        // IEND
        WritePngChunk(output, "IEND", Array.Empty<byte>());

        return output.ToArray();
    }

    private static void WritePngChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        WriteBigEndian(stream, data.Length);
        stream.Write(typeBytes);
        stream.Write(data);
        WriteBigEndian(stream, (int)Crc32.Compute(typeBytes, data));
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteBigEndian(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static class Crc32
    {
        private static readonly uint[] Table = new uint[256];

        static Crc32()
        {
            for (uint i = 0; i < 256; i++)
            {
                var c = i;
                for (var j = 0; j < 8; j++)
                {
                    c = (c & 1) == 1 ? (0xEDB88320 ^ (c >> 1)) : (c >> 1);
                }
                Table[i] = c;
            }
        }

        public static uint Compute(byte[] type, byte[] data)
        {
            var c = 0xFFFFFFFF;
            foreach (var b in type)
            {
                c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
            }
            foreach (var b in data)
            {
                c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
            }
            return c ^ 0xFFFFFFFF;
        }
    }
}
