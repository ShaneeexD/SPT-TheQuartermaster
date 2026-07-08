using SPTarkov.Server.Core.Models.Common;

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
        public const int ListingDurationHours = 168;
        public const int ListingDurationSeconds = ListingDurationHours * 3600;
    }

    public static class Seller
    {
        public const string AnonymizationSalt = "";
    }

    public static class Timings
    {
        public const int ExpiredCleanupIntervalMinutes = 5;
    }

    public static class FirestoreCollections
    {
        public const string Listings = "quartermaster_listings";
        public const string Meta = "quartermaster_meta";
        public const string Bans = "quartermaster_bans";
        public const string Config = "quartermaster_config";
    }
}
