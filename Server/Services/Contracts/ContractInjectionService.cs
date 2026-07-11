using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models.Contracts;
namespace TheQuartermaster.Server.Services.Contracts;

[Injectable(InjectionType.Singleton)]
public class ContractInjectionService(
    ISptLogger<ContractInjectionService> logger,
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    ItemHelper itemHelper,
    ModHelper modHelper
)
{
    private const string QuestFolderName = "db/CommunityQuests";

    public async Task InjectActiveContractsAsync(
        List<ContractScheduleEntry> activeEntries,
        Dictionary<string, ContractDefinition> definitionsById
    )
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var questOutputDir = Path.Combine(modPath, QuestFolderName.Replace('/', Path.DirectorySeparatorChar));

        if (activeEntries.Count == 0)
        {
            RemoveExpiredQuestSections(questOutputDir, activeEntries);
            await wttCommon.CustomQuestService.CreateCustomQuests(Assembly.GetExecutingAssembly(), QuestFolderName);
            return;
        }

        try
        {
            if (Directory.Exists(questOutputDir))
            {
                Directory.Delete(questOutputDir, true);
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[TheQuartermaster] Failed to clean previous community quests: {ex.Message}");
        }

        var traderId = QuartermasterConstants.TraderId.ToString();
        var count = ContractQuestBuilder.BuildQuestFiles(
            questOutputDir,
            traderId,
            activeEntries,
            definitionsById,
            itemHelper
        );

        logger.Info($"[TheQuartermaster] Built {count} community quest file(s) for trader {traderId}.");

        await wttCommon.CustomQuestService.CreateCustomQuests(Assembly.GetExecutingAssembly(), QuestFolderName);
        logger.Info("[TheQuartermaster] Injected community quests via WTT CustomQuestService.");
    }

    private void RemoveExpiredQuestSections(string questOutputDir, List<ContractScheduleEntry> activeEntries)
    {
        var activeQuestIds = activeEntries
            .Where(e => !string.IsNullOrWhiteSpace(e.QuestId))
            .Select(e => e.QuestId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var traderDir = Path.Combine(questOutputDir, QuartermasterConstants.TraderId.ToString());
        var questsFile = Path.Combine(traderDir, "Quests", "quest_definitions.json");
        var localesFile = Path.Combine(traderDir, "Locales", "en.json");

        if (!File.Exists(questsFile))
        {
            return;
        }

        try
        {
            var questsJson = File.ReadAllText(questsFile);
            var questRoot = JsonSerializer.Deserialize<JsonObject>(questsJson);
            if (questRoot is null)
            {
                return;
            }

            var removed = new List<string>();
            foreach (var property in questRoot.ToList())
            {
                if (!activeQuestIds.Contains(property.Key))
                {
                    questRoot.Remove(property.Key);
                    removed.Add(property.Key);
                }
            }

            if (removed.Count == 0)
            {
                return;
            }

            var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(questsFile, questRoot.ToJsonString(jsonOpts));

            if (File.Exists(localesFile))
            {
                var localesJson = File.ReadAllText(localesFile);
                var localeRoot = JsonSerializer.Deserialize<JsonObject>(localesJson);
                if (localeRoot is not null)
                {
                    foreach (var questId in removed)
                    {
                        var prefix = $"{questId} ";
                        var keysToRemove = localeRoot
                            .Where(p => p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            .Select(p => p.Key)
                            .ToList();
                        foreach (var key in keysToRemove)
                        {
                            localeRoot.Remove(key);
                        }
                    }

                    File.WriteAllText(localesFile, localeRoot.ToJsonString(jsonOpts));
                }
            }

            logger.Info($"[TheQuartermaster] Removed {removed.Count} expired quest section(s) from local quest files.");
        }
        catch (Exception ex)
        {
            logger.Warning($"[TheQuartermaster] Failed to clean expired local quest files: {ex.Message}");
        }
    }
}
