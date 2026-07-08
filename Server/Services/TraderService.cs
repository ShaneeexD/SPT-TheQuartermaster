using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Server;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using TheQuartermaster.Server.Models;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class TraderService(
    ISptLogger<TraderService> logger,
    ConfigService configService,
    FirestoreService firestoreService,
    ItemHelper itemHelper,
    DatabaseService databaseService,
    ConfigServer configServer,
    ItemCloneService itemCloneService,
    ImageRouter? imageRouter = null
)
{
    private readonly TraderConfig _traderConfig = configServer.GetConfig<TraderConfig>();
    private readonly Dictionary<MongoId, string> _assortIdToListingId = new();
    private readonly List<QuartermasterListing> _activeListings = [];
    private Trader? _trader;

    public async Task RegisterTrader(string modPath)
    {
        if (!configService.Config.ModEnabled)
        {
            logger.Info("[TheQuartermaster] Mod disabled, skipping trader registration.");
            return;
        }

        var traderId = QuartermasterConstants.TraderId;
        if (databaseService.GetTables().Traders.ContainsKey(traderId))
        {
            logger.Warning($"[TheQuartermaster] Trader {traderId} already registered, skipping.");
            return;
        }

        try
        {
            _activeListings.Clear();
            _assortIdToListingId.Clear();

            if (firestoreService.IsEnabled)
            {
                var listings = await firestoreService.GetActiveListingsAsync();
                _activeListings.AddRange(listings.Where(l => !configService.Config.VanillaItemsOnly || l.IsVanilla));
                logger.Info($"[TheQuartermaster] Loaded {_activeListings.Count} active listings from Firestore.");
            }

            var traderBase = BuildTraderBase(modPath);
            _trader = BuildTrader(traderBase);

            AddTraderLocales(traderBase);
            RegisterAvatarRoute(modPath, traderBase);
            SetTraderUpdateTime(traderBase);

            databaseService.GetTables().Traders[traderId] = _trader;

            await RefreshAssort();

            logger.Info($"[TheQuartermaster] Trader '{traderBase.Nickname}' registered with {_trader.Assort.Items.Count} items.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to register trader: {ex.Message}", ex);
        }
    }

    public string? GetListingIdForAssortItem(MongoId assortItemId)
    {
        _assortIdToListingId.TryGetValue(assortItemId, out var listingId);
        return listingId;
    }

    public QuartermasterListing? GetListing(string? listingId)
    {
        if (string.IsNullOrWhiteSpace(listingId))
        {
            return null;
        }

        return _activeListings.FirstOrDefault(l => l.Id == listingId);
    }

    private TraderBase BuildTraderBase(string modPath)
    {
        var allParents = new HashSet<MongoId>(databaseService.GetItems().Values.Select(i => i.Parent).Where(p => !string.IsNullOrWhiteSpace(p)));
        var allIds = new HashSet<MongoId>(databaseService.GetItems().Keys);
        var prohibited = new HashSet<MongoId>(QuartermasterConstants.ExcludedTpls.Select(t => new MongoId(t)));

        return new TraderBase
        {
            Id = QuartermasterConstants.TraderId,
            AvailableInRaid = false,
            Avatar = $"/files/trader/avatar/{QuartermasterConstants.TraderId}.jpg",
            BalanceRub = 999999999,
            BalanceDollar = 0,
            BalanceEuro = 0,
            BuyerUp = true,
            Currency = CurrencyType.RUB,
            CustomizationSeller = false,
            Discount = 0,
            DiscountEnd = 0,
            GridHeight = 500,
            ProhibitedItemsSellModifier = 0,
            Insurance = new TraderInsurance
            {
                Availability = false,
                MinPayment = 0,
                MinReturnHour = 0,
                MaxReturnHour = 0,
                MaxStorageTime = 0,
                ExcludedCategory = []
            },
            ItemsBuy = new ItemBuyData
            {
                Category = allParents,
                IdList = []
            },
            ItemsBuyProhibited = new ItemBuyData
            {
                Category = [],
                IdList = prohibited
            },
            IsAvailableInPVE = true,
            IsCanTransferItems = false,
            IsCanTransferItemsFromPve = false,
            TransferableItems = new ItemBuyData { Category = [], IdList = [] },
            ProhibitedTransferableItems = new ItemBuyData { Category = [], IdList = [] },
            Location = QuartermasterConstants.TraderLocation,
            LoyaltyLevels =
            [
                new TraderLoyaltyLevel
                {
                    MinLevel = 1,
                    MinSalesSum = 0,
                    MinStanding = 0,
                    BuyPriceCoefficient = 1.0,
                    InsurancePriceCoefficient = 1,
                    RepairPriceCoefficient = 1,
                    ExchangePriceCoefficient = 1,
                    HealPriceCoefficient = 1
                }
            ],
            MainDialogue = string.Empty,
            Medic = false,
            Name = QuartermasterConstants.TraderFullName,
            NextResupply = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nickname = QuartermasterConstants.TraderNickname,
            Repair = new TraderRepair
            {
                Availability = false,
                Currency = "RUB",
                CurrencyCoefficient = 1,
                ExcludedCategory = [],
                ExcludedIdList = [],
                Quality = 1,
                PriceRate = 1
            },
            SellCategory = [],
            Surname = string.Empty,
            UnlockedByDefault = true
        };
    }

    private Trader BuildTrader(TraderBase traderBase)
    {
        return new Trader
        {
            Base = traderBase,
            Assort = new TraderAssort
            {
                Items = [],
                BarterScheme = new Dictionary<MongoId, List<List<BarterScheme>>>(),
                LoyalLevelItems = new Dictionary<MongoId, int>()
            },
            Dialogue = new Dictionary<string, List<string>?>
            {
                ["intro"] = ["Welcome to the Quartermaster."],
                ["soldItems"] = ["Item sold successfully."],
                ["itemsSold"] = ["Your item has found a buyer."],
                ["buySuccess"] = ["Purchase confirmed."],
                ["buyFailed"] = ["Purchase failed."]
            },
            QuestAssort = new Dictionary<string, Dictionary<MongoId, MongoId>>
            {
                ["started"] = new(),
                ["success"] = new(),
                ["fail"] = new()
            }
        };
    }

    public async Task RefreshAssort()
    {
        if (!configService.Config.ModEnabled)
        {
            return;
        }

        if (_trader is null)
        {
            return;
        }

        var traderId = QuartermasterConstants.TraderId;
        if (!databaseService.GetTables().Traders.ContainsKey(traderId))
        {
            return;
        }

        _activeListings.Clear();
        _assortIdToListingId.Clear();

        if (firestoreService.IsEnabled)
        {
            await firestoreService.CleanupExpiredListingsAsync();
            var listings = await firestoreService.GetActiveListingsAsync();
            _activeListings.AddRange(listings.Where(l => !configService.Config.VanillaItemsOnly || l.IsVanilla));
            logger.Info($"[TheQuartermaster] Refreshed {_activeListings.Count} active listings from Firestore.");
        }

        var newAssort = BuildAssort();
        _trader.Assort = newAssort;
        _trader.Base.NextResupply = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1;

        logger.Info($"[TheQuartermaster] Assort refreshed with {newAssort.Items.Count} items.");
    }

    private TraderAssort BuildAssort()
    {
        var assort = new TraderAssort
        {
            Items = [],
            BarterScheme = new Dictionary<MongoId, List<List<BarterScheme>>>(),
            LoyalLevelItems = new Dictionary<MongoId, int>()
        };

        foreach (var listing in _activeListings)
        {
            if (string.IsNullOrWhiteSpace(listing.RootTpl) || !itemHelper.IsItemInDb(new MongoId(listing.RootTpl)))
            {
                logger.Warning($"[TheQuartermaster] Skipping listing {listing.Id}: root tpl {listing.RootTpl} not in runtime DB.");
                continue;
            }

            var itemTree = itemCloneService.DeserializeItemTree(listing.ItemTreeJson);
            if (itemTree is null || itemTree.Count == 0)
            {
                logger.Warning($"[TheQuartermaster] Skipping listing {listing.Id}: could not deserialize item tree.");
                continue;
            }

            var clonedTree = itemCloneService.CloneAndRemap(itemTree);
            var root = clonedTree[0];
            var assortItemId = root.Id;

            _assortIdToListingId[assortItemId] = listing.Id!;

            root.Upd ??= new Upd { StackObjectsCount = 1 };
            root.Upd.BuyRestrictionMax = 1;
            root.Upd.BuyRestrictionCurrent = 0;

            assort.Items.AddRange(clonedTree);
            assort.BarterScheme[assortItemId] =
            [
                [new BarterScheme { Template = Money.ROUBLES, Count = listing.MarketPrice }]
            ];
            assort.LoyalLevelItems[assortItemId] = 1;
        }

        return assort;
    }

    private void AddTraderLocales(TraderBase traderBase)
    {
        var locales = databaseService.GetTables().Locales.Global;
        var traderId = traderBase.Id;

        foreach (var (_, localeData) in locales)
        {
            localeData.AddTransformer(ld =>
            {
                ld ??= new Dictionary<string, string>();
                ld[$"{traderId} FullName"] = QuartermasterConstants.TraderFullName;
                ld[$"{traderId} FirstName"] = QuartermasterConstants.TraderNickname;
                ld[$"{traderId} Nickname"] = QuartermasterConstants.TraderNickname;
                ld[$"{traderId} Location"] = QuartermasterConstants.TraderLocation;
                ld[$"{traderId} Description"] = "A global market for survivor gear.";
                return ld;
            });
        }
    }

    private void RegisterAvatarRoute(string modPath, TraderBase traderBase)
    {
        if (imageRouter is null)
        {
            return;
        }

        var avatarPath = System.IO.Path.Combine(modPath, "db", "avatar.jpg");
        if (File.Exists(avatarPath))
        {
            var routePath = $"/files/trader/avatar/{traderBase.Id}";
            imageRouter.AddRoute(routePath, avatarPath);
        }
    }

    private void SetTraderUpdateTime(TraderBase traderBase)
    {
        var updateTime = new UpdateTime
        {
            Name = traderBase.Nickname!,
            TraderId = traderBase.Id,
            Seconds = new MinMax<int>(1, 1)
        };
        _traderConfig.UpdateTime.Add(updateTime);
    }
}
