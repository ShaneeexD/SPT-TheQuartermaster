using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using TheQuartermaster.Server.Models.Contracts;
namespace TheQuartermaster.Server.Services.Contracts;

[Injectable(InjectionType.Singleton)]
public class ContractInjectionService(
    ISptLogger<ContractInjectionService> logger,
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    ItemHelper itemHelper,
    ModHelper modHelper,
    ImageRouter? imageRouter = null
)
{
    private const string QuestFolderName = "db/CommunityQuests";

    public async Task InjectActiveContractsAsync(
        List<ContractScheduleEntry> activeEntries,
        Dictionary<string, ContractDefinition> definitionsById
    )
    {
        if (activeEntries.Count == 0)
        {
            await wttCommon.CustomQuestService.CreateCustomQuests(Assembly.GetExecutingAssembly(), QuestFolderName);
            return;
        }

        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var questOutputDir = Path.Combine(modPath, QuestFolderName.Replace('/', Path.DirectorySeparatorChar));

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
            itemHelper,
            imageRouter
        );

        logger.Info($"[TheQuartermaster] Built {count} community quest file(s) for trader {traderId}.");

        await wttCommon.CustomQuestService.CreateCustomQuests(Assembly.GetExecutingAssembly(), QuestFolderName);
        logger.Info("[TheQuartermaster] Injected community quests via WTT CustomQuestService.");
    }
}
