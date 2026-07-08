using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Extensions;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class ItemCloneService(
    ISptLogger<ItemCloneService> logger,
    JsonUtil jsonUtil,
    ItemHelper itemHelper,
    ICloner cloner
)
{
    public List<Item>? DeserializeItemTree(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return jsonUtil.Deserialize<List<Item>>(json);
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to deserialize item tree: {ex.Message}", ex);
            return null;
        }
    }

    public List<Item> CloneAndRemap(List<Item> itemTree)
    {
        var clone = cloner.Clone(itemTree) ?? new List<Item>();

        // SPT's ReplaceIDs mutates the list in place and updates child parentIds.
        itemHelper.ReplaceIDs(clone, null, null);

        // Ensure the root item is parented to the stash root (PlaceItemInInventory will set exact placement).
        var root = clone.FirstOrDefault();
        if (root is not null)
        {
            root.ParentId = "hideout";
            root.SlotId = "hideout";
            root.Location = null;
        }

        return clone;
    }

    public string SerializeItemTree(List<Item> itemTree)
    {
        return jsonUtil.Serialize(itemTree) ?? "[]";
    }

    public List<Item> GetItemTreeFromInventory(PmcData pmcData, MongoId rootItemId)
    {
        return pmcData.Inventory?.Items?.GetItemWithChildren(rootItemId) ?? new List<Item>();
    }

    public List<Item> FlattenItemTree(List<Item> itemTree)
    {
        return itemTree;
    }
}
