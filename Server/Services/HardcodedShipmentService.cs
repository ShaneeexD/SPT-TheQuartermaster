using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using SPTarkov.Server.Core.Utils;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class HardcodedShipmentService(
    ISptLogger<HardcodedShipmentService> logger,
    DatabaseService databaseService,
    CustomItemService customItemService,
    RandomUtil randomUtil
)
{
    private const string BaseCrateTpl = "66582972ac60f009f270d2aa";

    public static readonly string MedicalShipmentTpl = "66582972ac60f009f270d2b1";
    public static readonly string GrenadierShipmentTpl = "66582972ac60f009f270d2b2";
    public static readonly string RationsShipmentTpl = "66582972ac60f009f270d2b3";

    private static readonly Dictionary<string, ShipmentDef> Shipments = new()
    {
        [MedicalShipmentTpl] = new ShipmentDef
        {
            TemplateId = MedicalShipmentTpl,
            Name = "Quartermaster Medical Supplies",
            ShortName = "QM Medical",
            Description = "A sealed medical shipment assembled by The Quartermaster containing emergency medical equipment for frontline operatives.",
            Price = 190000,
            BundlePath = "assets/content/items/containers/item_container_meds/item_container_meds.bundle",
            MailMessage = "Your Quartermaster Medical Supplies shipment has arrived. Contents are packed inside.",
            Contents =
            [
                new ShipmentContent("5755356824597772cb798962", 2, 5),
                new ShipmentContent("5e8488fa988a8701445df1e4", 2, 5),
                new ShipmentContent("5e831507ea0a7c419c2f9bd9", 2, 4),
                new ShipmentContent("5af0454c86f7746bf20992e8", 1, 3),
                new ShipmentContent("544fb45d4bdc2dee738b4568", 1, 2),
                new ShipmentContent("5d02778e86f774203e7dedbe", 1, 2),
                new ShipmentContent("544fb37f4bdc2dee738b4567", 1, 2),
                new ShipmentContent("5af0548586f7743a532b7e99", 1, 2)
            ]
        },
        [GrenadierShipmentTpl] = new ShipmentDef
        {
            TemplateId = GrenadierShipmentTpl,
            Name = "Quartermaster Grenadier Package",
            ShortName = "QM Grenadier",
            Description = "A sealed shipment containing explosive ordnance and tactical grenades recovered through Quartermaster supply channels.",
            Price = 108000,
            BundlePath = "assets/content/items/containers/item_container_plates_case/item_container_plates_case.bundle",
            MailMessage = "Your Quartermaster Grenadier Package has arrived. Handle with care.",
            Contents =
            [
                new ShipmentContent("5710c24ad2720bc3458b45a3", 1, 3),
                new ShipmentContent("5448be9a4bdc2dfd2f8b456a", 1, 3),
                new ShipmentContent("5a0c27731526d80618476ac4", 1, 2),
                new ShipmentContent("5a2a57cfc4a2826c6e06d44a", 1, 2),
                new ShipmentContent("5e32f56fcb6d5863cc5e5ee4", 2, 6),
                new ShipmentContent("5656eb674bdc2d35148b457c", 2, 6)
            ]
        },
        [RationsShipmentTpl] = new ShipmentDef
        {
            TemplateId = RationsShipmentTpl,
            Name = "Quartermaster Rations Pack",
            ShortName = "QM Rations",
            Description = "A sealed shipment containing food, drinks and provisions prepared for extended deployments.",
            Price = 156000,
            BundlePath = "assets/content/items/containers/item_container_food/item_container_food.bundle",
            MailMessage = "Your Quartermaster Rations Pack has arrived. Best consumed before deployment.",
            Contents =
            [
                new ShipmentContent("590c5d4b86f774784e1b9c45", 1, 3),
                new ShipmentContent("590c5f0d86f77413997acfab", 1, 2),
                new ShipmentContent("5448ff904bdc2d6f028b456e", 2, 5),
                new ShipmentContent("5448fee04bdc2dbc018b4567", 2, 5),
                new ShipmentContent("57513f07245977207e26a311", 1, 3),
                new ShipmentContent("5751435d24597720a27126d1", 1, 2),
                new ShipmentContent("59e3577886f774176a362503", 1, 2),
                new ShipmentContent("5af0484c86f7740f02001f7f", 1, 2)
            ]
        }
    };

    public static IEnumerable<ShipmentDef> GetAllShipments() => Shipments.Values;

    public void EnsureTemplates()
    {
        foreach (var shipment in Shipments.Values)
        {
            EnsureTemplate(shipment);
        }
    }

    private void EnsureTemplate(ShipmentDef shipment)
    {
        var items = databaseService.GetItems();
        if (items.ContainsKey(shipment.TemplateId))
        {
            return;
        }

        if (!items.TryGetValue(BaseCrateTpl, out var baseTpl))
        {
            logger.DebugWarning("[TheQuartermaster] Base crate template not found; cannot create hardcoded shipment clones.");
            return;
        }

        var handbook = databaseService.GetHandbook().Items.FirstOrDefault(h => h.Id.ToString() == BaseCrateTpl);
        var details = new NewItemFromCloneDetails
        {
            NewId = shipment.TemplateId,
            ItemTplToClone = new MongoId(BaseCrateTpl),
            ParentId = baseTpl.Parent.ToString(),
            HandbookParentId = handbook is not null ? handbook.ParentId.ToString() : "5b5f6fa186f77409407a7eb7",
            HandbookPriceRoubles = shipment.Price,
            FleaPriceRoubles = shipment.Price,
            OverrideProperties = new TemplateItemProperties
            {
                BackgroundColor = "yellow",
                CanSellOnRagfair = false
            },
            Locales = new Dictionary<string, LocaleDetails>
            {
                ["en"] = new LocaleDetails
                {
                    Name = shipment.Name,
                    ShortName = shipment.ShortName,
                    Description = shipment.Description
                }
            }
        };

        var result = customItemService.CreateItemFromClone(details);
        if (result.Success != true)
        {
            logger.DebugWarning($"[TheQuartermaster] Shipment clone failed for {shipment.Name}: {string.Join(", ", result.Errors ?? [])}");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(shipment.BundlePath) && items.TryGetValue(shipment.TemplateId, out var tpl) && tpl.Properties != null)
            {
                if (tpl.Properties.Prefab != null)
                {
                    tpl.Properties.Prefab.Path = shipment.BundlePath;
                }
                if (tpl.Properties.UsePrefab != null)
                {
                    tpl.Properties.UsePrefab.Path = shipment.BundlePath;
                }
            }
            logger.DebugInfo($"[TheQuartermaster] Created hardcoded shipment template: {shipment.Name} ({shipment.TemplateId})");
        }
    }

    public List<Item> BuildShipmentContents(string templateId)
    {
        if (!Shipments.TryGetValue(templateId, out var shipment))
        {
            return [];
        }

        var crateId = new MongoId();
        var items = new List<Item>
        {
            new()
            {
                Id = crateId,
                Template = templateId,
                SlotId = "hideout",
                Upd = new Upd { SpawnedInSession = false }
            }
        };

        foreach (var content in shipment.Contents)
        {
            var qty = randomUtil.GetInt(content.Min, content.Max);
            for (var i = 0; i < qty; i++)
            {
                items.Add(new Item
                {
                    Id = new MongoId(),
                    Template = content.Tpl,
                    ParentId = crateId.ToString(),
                    SlotId = "main",
                    Location = null,
                    Upd = new Upd { SpawnedInSession = true }
                });
            }
        }

        return items;
    }

    public static bool IsShipmentTemplate(string tpl)
    {
        return Shipments.ContainsKey(tpl);
    }

    public sealed class ShipmentDef
    {
        public string TemplateId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Price { get; set; }
        public string? BundlePath { get; set; }
        public string MailMessage { get; set; } = string.Empty;
        public List<ShipmentContent> Contents { get; set; } = [];
    }

    public sealed class ShipmentContent
    {
        public string Tpl { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }

        public ShipmentContent(string tpl, int min, int max)
        {
            Tpl = tpl;
            Min = min;
            Max = max;
        }
    }
}
