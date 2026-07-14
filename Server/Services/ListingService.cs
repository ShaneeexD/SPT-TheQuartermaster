using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services;
using TheQuartermaster.Server.Models;
using TheQuartermaster.Server.Services.Contracts;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class ListingService(
    ISptLogger<ListingService> logger,
    ConfigService configService,
    BackendConfigService backendConfigService,
    VanillaAllowlistService vanillaAllowlistService,
    ItemCompatibilityService itemCompatibilityService,
    ItemCloneService itemCloneService,
    ItemHelper itemHelper,
    HandbookHelper handbookHelper
)
{
    private readonly Dictionary<string, string> _sellerHashToSessionId = [];

    public QuartermasterListing? CreateListing(List<Item> itemTree, string sellerProfileId, string sellerSessionId)
    {
        if (!itemCompatibilityService.IsListingValidForUpload(itemTree))
        {
            return null;
        }

        var root = itemTree.First();

        foreach (var item in itemTree)
        {
            item.Upd ??= new Upd();
            item.Upd.SpawnedInSession = false;
        }

        var rootTemplate = itemHelper.GetItem(root.Template).Value;
        var name = rootTemplate?.Name ?? vanillaAllowlistService.GetVanillaName(root.Template) ?? root.Template.ToString();
        var shortName = rootTemplate?.Properties?.ShortName ?? name;

        var basePrice = CalculateBasePrice(itemTree);
        var qualityMultiplier = itemHelper.GetItemQualityModifierForItems(itemTree);
        var marketPrice = CalculateMarketPrice(basePrice, qualityMultiplier);

        if (marketPrice < QuartermasterConstants.Marketplace.MinPrice)
        {
            logger.DebugInfo($"[TheQuartermaster] Listing for {name} priced below minimum, adjusting to minimum.");
            marketPrice = QuartermasterConstants.Marketplace.MinPrice;
        }

        if (marketPrice > QuartermasterConstants.Marketplace.MaxPrice)
        {
            logger.DebugWarning($"[TheQuartermaster] Listing for {name} exceeds max price, capping.");
            marketPrice = QuartermasterConstants.Marketplace.MaxPrice;
        }

        var requiredTpls = itemCompatibilityService.GetRequiredTemplates(itemTree).Select(t => t.ToString()).ToList();
        var isVanilla = itemCompatibilityService.IsVanilla(itemTree);
        var now = DateTime.UtcNow;
        var expires = now.AddSeconds(QuartermasterConstants.Marketplace.ListingDurationSeconds);

        var listing = new QuartermasterListing
        {
            SellerHash = HashProfileId(sellerProfileId),
            RootTpl = root.Template.ToString(),
            RootName = name,
            ShortName = shortName,
            ItemTreeJson = itemCloneService.SerializeItemTree(itemTree),
            RequiredTpls = requiredTpls,
            BasePrice = basePrice,
            MarketPrice = marketPrice,
            QualityMultiplier = qualityMultiplier,
            IsVanilla = isVanilla,
            Status = ListingStatus.Active,
            CreatedAt = Timestamp.FromDateTime(now),
            ExpiresAt = Timestamp.FromDateTime(expires),
            ServerId = GetServerId(),
            Metadata = new Dictionary<string, string>
            {
                ["version"] = "1.0.0",
                ["source"] = "TheQuartermaster",
                ["seller_session_id"] = sellerSessionId
            }
        };

        _sellerHashToSessionId[listing.SellerHash!] = sellerSessionId;

        return listing;
    }

    public string? GetSellerSessionId(string? sellerHash)
    {
        if (string.IsNullOrWhiteSpace(sellerHash))
        {
            return null;
        }

        _sellerHashToSessionId.TryGetValue(sellerHash, out var sessionId);
        return sessionId;
    }

    public double CalculateBasePrice(List<Item> itemTree)
    {
        return itemTree.Sum(item => handbookHelper.GetTemplatePrice(item.Template));
    }

    public double CalculateMarketPrice(double basePrice, double qualityMultiplier)
    {
        var markup = 1.0 + (backendConfigService.Config.BaseMarkupPercent / 100.0);
        var price = basePrice * qualityMultiplier * markup;
        return Math.Round(price);
    }

    public string? GetItemName(MongoId tpl)
    {
        var template = itemHelper.GetItem(tpl).Value;
        if (!string.IsNullOrWhiteSpace(template?.Name))
        {
            return template.Name;
        }

        return vanillaAllowlistService.GetVanillaName(tpl);
    }

    private string HashProfileId(string profileId)
    {
        var salt = QuartermasterConstants.Seller.AnonymizationSalt;
        var input = string.IsNullOrEmpty(salt) ? profileId : $"{profileId}:{salt}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant();
        return hash[..Math.Min(hash.Length, 64)];
    }

    private string GetServerId()
    {
        return configService.SptRootName;
    }
}
