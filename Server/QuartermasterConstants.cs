using SPTarkov.Server.Core.Models.Common;
using Version = SemanticVersioning.Version;

namespace TheQuartermaster.Server;

public static class QuartermasterConstants
{
    public static readonly MongoId TraderId = new("66789abcde1234567890abcd");
    public static readonly MongoId TraderIdFriendly = new("66789abcdef1234567890abc");
    public const string TraderNickname = "Quartermaster";
    public const string TraderFullName = "The Quartermaster";
    public const string TraderLocation = "Hideout";
    public const string Currency = "RUB";

    public static readonly HashSet<string> ExcludedTpls = new(StringComparer.OrdinalIgnoreCase)
    {
        "5449016a4bdc2d6f028c4564",
        "544901bf4bdc2d0f3a8b65a4",
        "569668774bdc2da2298b4568",
        "5696686a4bdc2da3298b456a"
    };

    public static class Marketplace
    {
        public const int MinPrice = 100;
        public const int MaxPrice = 50_000_000;
        public const int MaxItemTreeSize = 100;
        public const int MaxAssortStackSize = 200_000;
        public const int ListingDurationMinutes = 20;
        public const int ListingDurationSeconds = ListingDurationMinutes * 60;

        public static readonly HashSet<string> AmmoParentIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "5485a8684bdc2da71d8b4567", // Ammo
            "543be5cb4bdc2deb348b4568" // AmmoBox
        };

        public static readonly HashSet<string> StackableParentIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "5485a8684bdc2da71d8b4567", // Ammo
            "543be5cb4bdc2deb348b4568", // AmmoBox
            "5448bc234bdc2d3c308b4569" // Magazine
        };
    }

    public static class Seller
    {
        public const string AnonymizationSalt = "";
    }

    public static class Timings
    {
        public const int ExpiredCleanupIntervalMinutes = 60;
        public const int RefreshCooldownMinutes = 5;
    }

    public static class Versions
    {
        public static readonly Version Current = new Version("1.0.5");
        public const string CurrentString = "1.0.5";
    }

    public static class FirestoreCollections
    {
        public const string Meta = "quartermaster_meta";
        public const string Bans = "quartermaster_bans";
        public const string Config = "quartermaster_config";
        public const string ContractDefinitions = "quartermaster_contracts";
        public const string ContractSubmissions = "quartermaster_submissions";
        public const string ContractVotes = "quartermaster_votes";
        public const string ContractSchedule = "quartermaster_schedule";
    }

    public static class FirestoreConfig
    {
        public const string ContractConfig = "contract_config";
        public const string ContractVersion = "contract_version";
        public const string ModVersion = "mod_version";
        public const string ListingConfig = "listing_config";
    }
}
