using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using TheQuartermaster.Server.Services;

namespace TheQuartermaster.Server.Patches;

[Injectable(InjectionType.Singleton)]
public class RewardCrateOpenPatch : AbstractPatch
{
    private static InventoryHelper? _inventoryHelper;
    private static ISptLogger<RewardCrateOpenPatch>? _logger;

    private static readonly HashSet<string> RewardCrateTemplateIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "6658892e6e007c6f33662002",
        "665732f4464c4b4ba4670fa9",
        "66582972ac60f009f270d2aa",
        "66789abcde1234567890abce"
    };

    public static void SetDependencies(InventoryHelper inventoryHelper, ISptLogger<RewardCrateOpenPatch> logger)
    {
        _inventoryHelper = inventoryHelper;
        _logger = logger;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.OpenRandomLootContainer));
    }

    [PatchPrefix]
    private static bool Prefix(
        PmcData pmcData,
        OpenRandomLootContainerRequestData request,
        MongoId sessionId,
        ItemEventRouterResponse output,
        ref bool __state)
    {
        __state = false;

        if (_inventoryHelper is null)
        {
            return true;
        }

        var openedItem = pmcData.Inventory?.Items?.FirstOrDefault(item => item.Id == request.Item);
        if (openedItem is null)
        {
            return true;
        }

        var templateId = openedItem.Template.ToString();
        if (!RewardCrateTemplateIds.Contains(templateId))
        {
            return true;
        }

        var childItems = pmcData.Inventory?.Items?
            .Where(item => item.ParentId == openedItem.Id)
            .ToList();

        if (childItems is null || childItems.Count == 0)
        {
            return true;
        }

        try
        {
            var allItems = pmcData.Inventory?.Items ?? new List<Item>();
            var rewards = new List<List<Item>>();

            foreach (var child in childItems)
            {
                var cloneTree = CloneItemTree(child, allItems);
                rewards.Add(cloneTree);
            }

            var foundInRaid = childItems.Any(c => c.Upd?.SpawnedInSession ?? false);
            var addItemsRequest = new AddItemsDirectRequest
            {
                ItemsWithModsToAdd = rewards,
                FoundInRaid = foundInRaid,
                Callback = null,
                UseSortingTable = true,
            };

            _inventoryHelper.AddItemsToStash(sessionId, addItemsRequest, pmcData, output);

            if (output.Warnings?.Count > 0)
            {
                _logger?.Error($"[TheQuartermaster] Failed to add reward crate contents to stash for session {sessionId}.");
                return false;
            }

            _inventoryHelper.RemoveItem(pmcData, request.Item, sessionId, output);

            __state = true;
            _logger?.DebugInfo("[TheQuartermaster] Reward crate opened with predefined contents for session " + sessionId.ToString() + ".");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.Error($"[TheQuartermaster] RewardCrateOpenPatch error: {ex.Message}", ex);
            return true;
        }
    }

    private static List<Item> CloneItemTree(Item root, List<Item> allItems)
    {
        var idMap = new Dictionary<string, MongoId>();
        var result = new List<Item>();
        CloneItemTreeRecursive(root, allItems, null, "hideout", result, idMap);
        return result;
    }

    private static void CloneItemTreeRecursive(Item item, List<Item> allItems, MongoId? newParentId, string slotId, List<Item> result, Dictionary<string, MongoId> idMap)
    {
        var newId = new MongoId();
        idMap[item.Id.ToString()] = newId;

        result.Add(new Item
        {
            Id = newId,
            Template = item.Template,
            ParentId = newParentId?.ToString(),
            SlotId = slotId,
            Location = null,
            Upd = item.Upd
        });

        var children = allItems.Where(i => i.ParentId == item.Id.ToString()).ToList();
        foreach (var child in children)
        {
            CloneItemTreeRecursive(child, allItems, newId, child.SlotId ?? "main", result, idMap);
        }
    }
}
