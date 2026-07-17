using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
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
using TheQuartermaster.Server.Services.Contracts;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class TraderService(
    ISptLogger<TraderService> logger,
    ConfigService configService,
    BackendConfigService backendConfigService,
    MarketplaceService marketplaceService,
    ItemHelper itemHelper,
    DatabaseService databaseService,
    ProfileHelper profileHelper,
    ConfigServer configServer,
    ItemCloneService itemCloneService,
    ImageRouter? imageRouter = null
)
{
    private readonly TraderConfig _traderConfig = configServer.GetConfig<TraderConfig>();
    private readonly Dictionary<MongoId, AssortStackInfo> _assortIdToStackInfo = new();
    private readonly List<QuartermasterListing> _activeListings = [];
    private Trader? _trader;

    private sealed class ProcessedListing
    {
        public QuartermasterListing? Listing { get; set; }
        public List<Item> ItemTree { get; set; } = [];
        public Item? Root { get; set; }
        public int Quantity { get; set; }
        public bool IsStackable { get; set; }
    }

    public sealed class AssortStackInfo
    {
        public double BuyRestrictionMax { get; set; }
        public List<AssortListingAllocation> Allocations { get; set; } = [];
    }

    public sealed class AssortListingAllocation
    {
        public string ListingId { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public async Task RegisterTrader(string modPath)
    {
        if (!configService.Config.ModEnabled)
        {
            logger.DebugInfo("[TheQuartermaster] Mod disabled, skipping trader registration.");
            return;
        }

        var traderId = QuartermasterConstants.TraderId;
        if (databaseService.GetTables().Traders.ContainsKey(traderId))
        {
            logger.DebugWarning($"[TheQuartermaster] Trader {traderId} already registered, skipping.");
            return;
        }

        try
        {
            _activeListings.Clear();
            _assortIdToStackInfo.Clear();

            if (marketplaceService.IsEnabled)
            {
                var listings = await marketplaceService.GetActiveListingsAsync();
                _activeListings.AddRange(listings.Where(l => !backendConfigService.Config.VanillaItemsOnly || l.IsVanilla));
                logger.DebugInfo($"[TheQuartermaster] Loaded {_activeListings.Count} active listings into trader assortment.");
            }

            var buyFilters = await marketplaceService.GetBuyFiltersAsync();
            var traderBase = BuildTraderBase(buyFilters);
            _trader = BuildTrader(traderBase);

            AddTraderLocales(traderBase);
            RegisterAvatarRoute(modPath, traderBase);
            SetTraderUpdateTime(traderBase);

            databaseService.GetTables().Traders[traderId] = _trader;

            await RefreshAssort();

            logger.DebugInfo($"[TheQuartermaster] Trader '{traderBase.Nickname}' registered with {_trader.Assort.Items.Count} items.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to register trader: {ex.Message}", ex);
        }
    }

    public string? GetListingIdForAssortItem(MongoId assortItemId)
    {
        _assortIdToStackInfo.TryGetValue(assortItemId, out var info);
        return info?.Allocations.FirstOrDefault()?.ListingId;
    }

    public AssortStackInfo? GetStackInfoForAssortItem(MongoId assortItemId)
    {
        _assortIdToStackInfo.TryGetValue(assortItemId, out var info);
        return info;
    }

    public List<AssortListingAllocation>? GetAllocationsForAssortItem(MongoId assortItemId)
    {
        return GetStackInfoForAssortItem(assortItemId)?.Allocations;
    }

    public double? GetBuyRestrictionMaxForAssortItem(MongoId assortItemId)
    {
        return GetStackInfoForAssortItem(assortItemId)?.BuyRestrictionMax;
    }

    public double GetBuyPriceMultiplier()
    {
        if (_trader is null)
        {
            return 0.6;
        }

        var coefficient = (_trader.Base.LoyaltyLevels ?? []).FirstOrDefault()?.BuyPriceCoefficient ?? 40;
        return (100 - coefficient) / 100.0;
    }

    public QuartermasterListing? GetListing(string? listingId)
    {
        if (string.IsNullOrWhiteSpace(listingId))
        {
            return null;
        }

        return _activeListings.FirstOrDefault(l => l.Id == listingId);
    }

    public void OnListingUploaded(QuartermasterListing listing)
    {
        if (_trader is null || string.IsNullOrWhiteSpace(listing.Id))
        {
            return;
        }

        if (backendConfigService.Config.VanillaItemsOnly && !listing.IsVanilla)
        {
            return;
        }

        _activeListings.RemoveAll(l => l.Id == listing.Id);
        _activeListings.Add(listing);

        var (markup, loyaltyLevel) = ResolveMarkupAndLevel(null);
        _trader.Assort = BuildAssort(markup, loyaltyLevel);
        _trader.Base.NextResupply = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1;

        logger.DebugInfo($"[TheQuartermaster] Added listing {listing.Id} to local assort; {_activeListings.Count} active listings.");
    }

    public void OnListingPurchased(string listingId, int quantityPurchased)
    {
        if (_trader is null || string.IsNullOrWhiteSpace(listingId))
        {
            return;
        }

        var listing = _activeListings.FirstOrDefault(l => l.Id == listingId);
        if (listing is null)
        {
            return;
        }

        // If the listing still has remaining quantity, we leave it in the assort.
        // The RTDB state tracks the real remaining quantity; the next full refresh will correct the assort count.
        // For fully sold listings, remove from the local list so the item disappears immediately.
        var state = marketplaceService.GetListingAsync(listingId).GetAwaiter().GetResult();
        if (state is null || state.Status == ListingStatus.Sold || state.ExpiresAt?.ToDateTime() < DateTime.UtcNow)
        {
            _activeListings.RemoveAll(l => l.Id == listingId);
        }

        var (markup, loyaltyLevel) = ResolveMarkupAndLevel(null);
        _trader.Assort = BuildAssort(markup, loyaltyLevel);
        _trader.Base.NextResupply = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1;

        logger.DebugInfo($"[TheQuartermaster] Updated local assort after purchase of {listingId}; {_activeListings.Count} active listings.");
    }

    private TraderBase BuildTraderBase(RtdbBuyFilters buyFilters)
    {
        var allParents = new HashSet<MongoId>(databaseService.GetItems().Values.Select(i => i.Parent).Where(p => !string.IsNullOrWhiteSpace(p)));

        var buyCategories = ParseMongoIdList(buyFilters.BuyCategories);
        var buyItems = ParseMongoIdList(buyFilters.BuyItems);
        var buyProhibitedCategories = ParseMongoIdList(buyFilters.BuyProhibitedCategories);
        var buyProhibitedItems = ParseMongoIdList(buyFilters.BuyProhibitedItems);

        // If nothing is configured, default to buying every category.
        var itemsBuyCategory = buyCategories.Count > 0 || buyItems.Count > 0
            ? buyCategories
            : allParents;

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
                Category = itemsBuyCategory,
                IdList = buyItems
            },
            ItemsBuyProhibited = new ItemBuyData
            {
                Category = buyProhibitedCategories,
                IdList = buyProhibitedItems
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
                    BuyPriceCoefficient = 45,
                    InsurancePriceCoefficient = 1,
                    RepairPriceCoefficient = 1,
                    ExchangePriceCoefficient = 1,
                    HealPriceCoefficient = 1
                },
                new TraderLoyaltyLevel
                {
                    MinLevel = 15,
                    MinSalesSum = 2000000,
                    MinStanding = 0,
                    BuyPriceCoefficient = 45,
                    InsurancePriceCoefficient = 1,
                    RepairPriceCoefficient = 1,
                    ExchangePriceCoefficient = 1,
                    HealPriceCoefficient = 1
                },
                new TraderLoyaltyLevel
                {
                    MinLevel = 30,
                    MinSalesSum = 18000000,
                    MinStanding = 0,
                    BuyPriceCoefficient = 45,
                    InsurancePriceCoefficient = 1,
                    RepairPriceCoefficient = 1,
                    ExchangePriceCoefficient = 1,
                    HealPriceCoefficient = 1
                },
                new TraderLoyaltyLevel
                {
                    MinLevel = 45,
                    MinSalesSum = 30000000,
                    MinStanding = 0,
                    BuyPriceCoefficient = 45,
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

    public async Task RefreshAssort(MongoId? sessionId = null)
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
        _assortIdToStackInfo.Clear();

        if (marketplaceService.IsEnabled)
        {
            var listings = await marketplaceService.GetActiveListingsAsync();
            _activeListings.AddRange(listings.Where(l => !backendConfigService.Config.VanillaItemsOnly || l.IsVanilla));
            logger.Info($"[TheQuartermaster] Loaded {_activeListings.Count} active listings into trader assortment.");
        }

        var (markup, loyaltyLevel) = ResolveMarkupAndLevel(sessionId);
        var newAssort = BuildAssort(markup, loyaltyLevel);
        _trader.Assort = newAssort;
        _trader.Base.NextResupply = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1;

        logger.Info($"[TheQuartermaster] Assort refreshed with {newAssort.Items.Count} items.");
    }

    private TraderAssort BuildAssort(double markup, int loyaltyLevel)
    {
        var assort = new TraderAssort
        {
            Items = [],
            BarterScheme = new Dictionary<MongoId, List<List<BarterScheme>>>(),
            LoyalLevelItems = new Dictionary<MongoId, int>()
        };

        var processed = new List<ProcessedListing>();
        foreach (var listing in _activeListings)
        {
            if (string.IsNullOrWhiteSpace(listing.RootTpl) || !itemHelper.IsItemInDb(new MongoId(listing.RootTpl)))
            {
                logger.DebugDebug($"[TheQuartermaster] Skipping listing {listing.Id}: root tpl {listing.RootTpl} not in runtime DB.");
                continue;
            }

            var itemTree = itemCloneService.DeserializeItemTree(listing.ItemTreeJson);
            if (itemTree is null || itemTree.Count == 0)
            {
                logger.DebugDebug($"[TheQuartermaster] Skipping listing {listing.Id}: could not deserialize item tree.");
                continue;
            }

            var root = itemTree[0];
            var quantity = (int)(root.Upd?.StackObjectsCount ?? 1);
            var isStackable = itemTree.Count == 1 && IsItemStackable(root.Template.ToString());

            processed.Add(new ProcessedListing
            {
                Listing = listing,
                ItemTree = itemTree,
                Root = root,
                Quantity = quantity,
                IsStackable = isStackable
            });
        }

        var stackableGroups = processed
            .Where(p => p.IsStackable)
            .GroupBy(p => p.Listing!.RootTpl)
            .ToList();

        foreach (var group in stackableGroups)
        {
            var chunk = new List<ProcessedListing>();
            var chunkCount = 0;
            foreach (var entry in group)
            {
                if (chunk.Count > 0 && chunkCount + entry.Quantity > QuartermasterConstants.Marketplace.MaxAssortStackSize)
                {
                    AddMergedStack(assort, chunk, chunkCount, markup, loyaltyLevel);
                    chunk.Clear();
                    chunkCount = 0;
                }

                chunk.Add(entry);
                chunkCount += entry.Quantity;
            }

            if (chunk.Count > 0)
            {
                AddMergedStack(assort, chunk, chunkCount, markup, loyaltyLevel);
            }
        }

        foreach (var entry in processed.Where(p => !p.IsStackable))
        {
            AddSingleItem(assort, entry, markup, loyaltyLevel);
        }

        return assort;
    }

    private bool IsItemStackable(string? tpl)
    {
        if (string.IsNullOrWhiteSpace(tpl))
        {
            return false;
        }

        var template = itemHelper.GetItem(new MongoId(tpl)).Value;
        if (template?.Properties?.StackMaxSize <= 1)
        {
            return false;
        }

        return QuartermasterConstants.Marketplace.StackableParentIds.Contains(template?.Parent.ToString() ?? string.Empty);
    }

    private void AddMergedStack(TraderAssort assort, List<ProcessedListing> listings, int totalQuantity, double markup, int loyaltyLevel)
    {
        var representative = listings[0];
        var clonedTree = itemCloneService.CloneAndRemap(representative.ItemTree);
        foreach (var item in clonedTree)
        {
            item.Upd ??= new Upd();
            item.Upd.SpawnedInSession = false;
        }
        var root = clonedTree[0];
        var assortItemId = root.Id;

        _assortIdToStackInfo[assortItemId] = new AssortStackInfo
        {
            BuyRestrictionMax = totalQuantity,
            Allocations = listings
                .Select(l => new AssortListingAllocation { ListingId = l.Listing!.Id!, Quantity = l.Quantity })
                .ToList()
        };

        root.Upd ??= new Upd();
        root.Upd.StackObjectsCount = totalQuantity;
        root.Upd.BuyRestrictionMax = totalQuantity;
        root.Upd.BuyRestrictionCurrent = 0;

        if (representative.Listing?.Metadata?.GetValueOrDefault("source") == QuartermasterConstants.Scavenged.ListingMetadataSource && root.Upd.Tag is null)
        {
            root.Upd.Tag = new UpdTag { Color = 0, Name = "~| " + representative.Listing.SellerName };
        }

        assort.Items.AddRange(clonedTree);
        var sellPrice = Math.Max(1, (int)Math.Round(GetItemTreeHandbookPrice(clonedTree) * markup));
        assort.BarterScheme[assortItemId] =
        [
            [new BarterScheme { Template = Money.ROUBLES, Count = sellPrice }]
        ];
        assort.LoyalLevelItems[assortItemId] = loyaltyLevel;

        logger.DebugInfo($"[TheQuartermaster] Merged {listings.Count} listings of {representative.Listing!.RootTpl} into stack of {totalQuantity}.");
    }

    private void AddSingleItem(TraderAssort assort, ProcessedListing entry, double markup, int loyaltyLevel)
    {
        var clonedTree = itemCloneService.CloneAndRemap(entry.ItemTree);
        foreach (var item in clonedTree)
        {
            item.Upd ??= new Upd();
            item.Upd.SpawnedInSession = false;
        }
        var root = clonedTree[0];
        var assortItemId = root.Id;

        _assortIdToStackInfo[assortItemId] = new AssortStackInfo
        {
            BuyRestrictionMax = entry.Quantity,
            Allocations =
            [
                new AssortListingAllocation { ListingId = entry.Listing!.Id!, Quantity = entry.Quantity }
            ]
        };

        root.Upd ??= new Upd();
        root.Upd.StackObjectsCount = entry.Quantity;
        root.Upd.BuyRestrictionMax = entry.Quantity;
        root.Upd.BuyRestrictionCurrent = 0;

        if (entry.Listing?.Metadata?.GetValueOrDefault("source") == QuartermasterConstants.Scavenged.ListingMetadataSource && root.Upd.Tag is null)
        {
            root.Upd.Tag = new UpdTag { Color = 0, Name = "~| " + entry.Listing.SellerName };
        }

        assort.Items.AddRange(clonedTree);
        var sellPrice = Math.Max(1, (int)Math.Round(GetItemTreeHandbookPrice(clonedTree) * markup));
        assort.BarterScheme[assortItemId] =
        [
            [new BarterScheme { Template = Money.ROUBLES, Count = sellPrice }]
        ];
        assort.LoyalLevelItems[assortItemId] = loyaltyLevel;
    }

    private long GetItemTreeHandbookPrice(List<Item> tree)
    {
        var total = 0L;
        foreach (var item in tree)
        {
            total += (long)itemHelper.GetStaticItemPrice(item.Template);
        }

        return total;
    }

    private (double Markup, int LoyaltyLevel) ResolveMarkupAndLevel(MongoId? sessionId)
    {
        if (!sessionId.HasValue || _trader is null)
        {
            return (1.05, 1);
        }

        try
        {
            var pmc = profileHelper.GetPmcProfile(sessionId.Value);
            if (pmc?.Info is null)
            {
                return (1.05, 1);
            }

            var level = GetCurrentLoyaltyLevel(pmc);
            var markup = level switch
            {
                4 => 1.02,
                3 => 1.03,
                2 => 1.04,
                _ => 1.05
            };

            return (markup, level);
        }
        catch (Exception ex)
        {
            logger.DebugWarning($"[TheQuartermaster] Could not resolve loyalty level for {sessionId}: {ex.Message}");
            return (1.05, 1);
        }
    }

    private int GetCurrentLoyaltyLevel(PmcData pmc)
    {
        if (_trader is null)
        {
            return 1;
        }

        var levels = _trader.Base.LoyaltyLevels ?? [];
        var salesSum = 0L;
        var standing = 0.0;
        if (pmc.TradersInfo is not null && pmc.TradersInfo.TryGetValue(QuartermasterConstants.TraderId, out var traderInfo))
        {
            salesSum = (long)(traderInfo.SalesSum ?? 0);
            standing = traderInfo.Standing ?? 0;
        }

        var playerLevel = pmc.Info?.Level ?? 0;
        for (var i = levels.Count - 1; i >= 0; i--)
        {
            var req = levels[i];
            if (playerLevel >= (req.MinLevel ?? 0) && salesSum >= (req.MinSalesSum ?? 0) && standing >= (req.MinStanding ?? 0))
            {
                return i + 1;
            }
        }

        return 1;
    }

    public bool CanBuyItem(MongoId tpl, MongoId? parentCategoryId)
    {
        if (_trader is null)
        {
            return false;
        }

        var itemsBuy = _trader.Base.ItemsBuy;
        var itemsBuyProhibited = _trader.Base.ItemsBuyProhibited;

        if (itemsBuyProhibited?.IdList?.Contains(tpl) == true || (parentCategoryId.HasValue && (itemsBuyProhibited?.Category?.Contains(parentCategoryId.Value) ?? false)))
        {
            return false;
        }

        if (itemsBuy?.IdList?.Contains(tpl) == true || (parentCategoryId.HasValue && (itemsBuy?.Category?.Contains(parentCategoryId.Value) ?? false)))
        {
            return true;
        }

        return false;
    }

    private static HashSet<MongoId> ParseMongoIdList(IEnumerable<string> ids)
    {
        return new HashSet<MongoId>(ids.Where(id => MongoId.IsValidMongoId(id)).Select(id => new MongoId(id)));
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

        var avatarPath = System.IO.Path.Combine(modPath, "Assets", "trader.png");
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
            Seconds = new MinMax<int>(300, 300)
        };
        _traderConfig.UpdateTime.Add(updateTime);
    }
}
