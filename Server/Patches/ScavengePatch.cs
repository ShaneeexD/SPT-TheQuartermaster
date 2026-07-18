using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using TheQuartermaster.Server.Models;
using TheQuartermaster.Server.Services;
using TheQuartermaster.Server.Services.Contracts;

namespace TheQuartermaster.Server.Patches;

[Injectable(InjectionType.Singleton)]
public class ScavengePatch : AbstractPatch
{
    private static ISptLogger<ScavengePatch>? _logger;
    private static ConfigService? _configService;
    private static BackendConfigService? _backendConfigService;
    private static ScavengedItemService? _scavengedItemService;
    private static ItemCloneService? _itemCloneService;
    private static DatabaseService? _databaseService;
    private static RandomUtil? _randomUtil;

    public ScavengePatch()
        : base("TheQuartermaster.ScavengePatch")
    {
    }

    public static void SetDependencies(
        ISptLogger<ScavengePatch> logger,
        ConfigService configService,
        BackendConfigService backendConfigService,
        ScavengedItemService scavengedItemService,
        ItemCloneService itemCloneService,
        DatabaseService databaseService,
        RandomUtil randomUtil
    )
    {
        _logger = logger;
        _configService = configService;
        _backendConfigService = backendConfigService;
        _scavengedItemService = scavengedItemService;
        _itemCloneService = itemCloneService;
        _databaseService = databaseService;
        _randomUtil = randomUtil;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(InRaidHelper), nameof(InRaidHelper.DeleteInventory));
    }

    [PatchPrefix]
    private static void Prefix(PmcData pmcData, MongoId sessionId)
    {
        try
        {
            if (_configService?.Config.ModEnabled != true)
            {
                return;
            }

            if (_configService?.Config.ScavengingEnabled != true)
            {
                return;
            }

            var config = _backendConfigService?.Config;
            if (config is null || !config.ScavengingEnabled)
            {
                return;
            }

            // Roll the chance
            if (_randomUtil is null || !_randomUtil.GetChance100(config.ScavengingChance * 100))
            {
                return;
            }

            var inventoryItems = pmcData.Inventory?.Items;
            if (inventoryItems is null || inventoryItems.Count == 0)
            {
                return;
            }

            var equipmentRootId = pmcData.Inventory?.Equipment;
            var questRaidItemContainerId = pmcData.Inventory?.QuestRaidItems;

            // Replicate GetInventoryItemsLostOnDeath: find root items (direct children of equipment or questRaidItems)
            // that are not kept after death. Only root items, not child attachments.
            var lostRootItems = inventoryItems.Where(item =>
            {
                // Must be a direct child of equipment or questRaidItems
                if (item.ParentId is null)
                {
                    return false;
                }

                if (item.ParentId != equipmentRootId && item.ParentId != questRaidItemContainerId)
                {
                    // Pocket items are lost on death too
                    if (!(item.SlotId?.StartsWith("pocket") ?? false))
                    {
                        return false;
                    }
                }

                // Exclude the virtual Pockets grid container (not a real item)
                if (item.Template.ToString() == "557596e64bdc2dc2118b4571")
                {
                    return false;
                }

                return true;
            }).ToList();

            if (lostRootItems.Count == 0)
            {
                return;
            }

            // Filter out insured items
            var insuredIds = pmcData.InsuredItems?
                .Where(i => i.ItemId is not null)
                .Select(i => i.ItemId!.Value)
                .ToHashSet() ?? new HashSet<MongoId>();

            // Filter out money/currency and quest items
            var dbItems = _databaseService?.GetItems();
            var candidates = lostRootItems.Where(item =>
            {
                if (insuredIds.Contains(item.Id))
                {
                    return false;
                }

                if (dbItems is not null && dbItems.TryGetValue(item.Template, out var template))
                {
                    var props = template.Properties;
                    if (props?.QuestItem == true)
                    {
                        return false;
                    }
                    if (props?.IsRagfairCurrency == true)
                    {
                        return false;
                    }
                    if (QuartermasterConstants.Marketplace.StackableParentIds.Contains(template?.Parent.ToString() ?? string.Empty))
                    {
                        return false;
                    }
                }

                return true;
            }).ToList();

            if (candidates.Count == 0)
            {
                return;
            }

            // Weighted selection: equipment slots get higher weight
            var equipmentWeight = config.ScavengingEquipmentWeight;
            var otherWeight = config.ScavengingOtherWeight;

            var weightedCandidates = new Dictionary<Item, double>();
            foreach (var item in candidates)
            {
                var isEquipment = QuartermasterConstants.Scavenged.EquipmentSlots.Contains(item.SlotId ?? string.Empty);
                weightedCandidates[item] = isEquipment ? equipmentWeight : otherWeight;
            }

            var selectedItem = SelectWeighted(weightedCandidates);
            if (selectedItem is null)
            {
                return;
            }

            // Get the full item tree (item + children)
            var itemTree = inventoryItems.GetItemWithChildren(selectedItem.Id);
            if (itemTree.Count == 0)
            {
                return;
            }

            var ownerName = pmcData.Info?.Nickname ?? "Unknown PMC";

            // Strip FIR (SpawnedInSession) from all items in the tree
            foreach (var item in itemTree)
            {
                if (item.Upd is not null)
                {
                    item.Upd.SpawnedInSession = false;
                }
            }

            // Tag the root item so the client can render the scavenged tag UI
            selectedItem.Upd ??= new Upd();
            selectedItem.Upd.Tag = new UpdTag { Color = 0, Name = "| " + ownerName };

            // Serialize
            var itemTreeJson = _itemCloneService?.SerializeItemTree(itemTree) ?? "[]";

            // Get item name for metadata
            string? rootName = null;
            if (dbItems is not null && dbItems.TryGetValue(selectedItem.Template, out var tpl))
            {
                rootName = tpl.Name;
            }

            var now = DateTime.UtcNow;
            var delayMinutes = config.ScavengingDelayMinutes > 0 ? config.ScavengingDelayMinutes : 30;
            var nowEpoch = new DateTimeOffset(now).ToUnixTimeSeconds();

            var scavengedItem = new ScavengedItem
            {
                ItemTreeJson = itemTreeJson,
                RootTpl = selectedItem.Template.ToString(),
                RootName = rootName,
                OriginalOwnerName = ownerName,
                AvailableAt = nowEpoch + (delayMinutes * 60),
                CreatedAt = nowEpoch,
                ServerId = sessionId.ToString()
            };

            _scavengedItemService?.SaveScavengedItemAsync(scavengedItem).GetAwaiter().GetResult();
#if DEBUG
            _logger?.DebugInfo($"[TheQuartermaster] Scavenged item from {ownerName}: {rootName ?? selectedItem.Template} (available in {delayMinutes}m)");
#endif
        }
        catch (Exception ex)
        {
            _logger?.Error($"[TheQuartermaster] ScavengePatch error: {ex.Message}", ex);
        }
    }

    private static Item? SelectWeighted(Dictionary<Item, double> weighted)
    {
        if (weighted.Count == 0)
        {
            return null;
        }

        if (weighted.Count == 1)
        {
            return weighted.Keys.First();
        }

        var total = weighted.Values.Sum();
        if (total <= 0)
        {
            return weighted.Keys.First();
        }

        var roll = _randomUtil?.GetDouble(0, total) ?? 0;
        double cumulative = 0;
        foreach (var (item, weight) in weighted)
        {
            cumulative += weight;
            if (roll <= cumulative)
            {
                return item;
            }
        }

        return weighted.Keys.Last();
    }
}
