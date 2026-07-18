using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using SPTarkov.Server.Core.Models.Spt.Mod;
using TheQuartermaster.Server.Models.Rewards;

namespace TheQuartermaster.Server.Services.Rewards;

[Injectable(InjectionType.Singleton)]
public class CommunityRewardService(
    ISptLogger<CommunityRewardService> logger,
    ConfigService configService,
    RewardFileService rewardFileService,
    MailSendService mailSendService,
    CustomItemService customItemService,
    DatabaseService databaseService
)
{
    private const string LastClaimedWeekKey = "quartermasterLastClaimedWeek";

    private const string Tier4TemplateId = "66789abcde1234567890abce";

    private static readonly Dictionary<int, string> TierTemplates = new()
    {
        [1] = "6658892e6e007c6f33662002",
        [2] = "665732f4464c4b4ba4670fa9",
        [3] = "66582972ac60f009f270d2aa"
    };

    public async Task TryClaimWeeklyReward(MongoId sessionId, SptProfile? fullProfile, PmcData? pmcData)
    {
        if (!configService.Config.ModEnabled || fullProfile is null || pmcData is null)
        {
            return;
        }

        try
        {
            var weeklyReward = await rewardFileService.GetWeeklyRewardAsync();
            if (weeklyReward is null || string.IsNullOrWhiteSpace(weeklyReward.Week) || string.IsNullOrWhiteSpace(weeklyReward.RewardId))
            {
                return;
            }

            var desiredWeek = EncodeWeek(weeklyReward.Week);
            var lastClaimedWeek = GetLastClaimedWeek(fullProfile);
            if (lastClaimedWeek == desiredWeek)
            {
                return;
            }

            var templateId = ResolveTemplateId(weeklyReward.Tier);
            if (string.IsNullOrWhiteSpace(templateId))
            {
                logger.DebugWarning($"[TheQuartermaster] Unknown reward tier {weeklyReward.Tier}; skipping claim.");
                return;
            }

            var crateId = new MongoId();
            var items = new List<Item>
            {
                new()
                {
                    Id = crateId,
                    Template = templateId,
                    SlotId = "hideout",
                    ParentId = null,
                    Upd = new Upd
                    {
                        Tag = new UpdTag
                        {
                            Name = weeklyReward.RewardId,
                            Color = weeklyReward.Tier
                        },
                        SpawnedInSession = false
                    },
                    Desc = weeklyReward.Week
                }
            };

            // Add child items inside the crate from the weekly reward contents
            foreach (var content in weeklyReward.Contents)
            {
                if (string.IsNullOrWhiteSpace(content.Tpl) || content.Count <= 0)
                {
                    continue;
                }

                items.Add(new Item
                {
                    Id = new MongoId(),
                    Template = content.Tpl,
                    ParentId = crateId,
                    SlotId = "main",
                    Location = null,
                    Upd = new Upd
                    {
                        StackObjectsCount = content.Count,
                        SpawnedInSession = content.FoundInRaid
                    }
                });
            }

            // Tier 4 uses the Tier 3 crate with a yellow highlight marker.
            if (weeklyReward.Tier == 4)
            {
                items[0].Upd ??= new Upd();
                items[0].Upd.Tag ??= new UpdTag();
                items[0].Upd.Tag.Name = $"{weeklyReward.RewardId}|yellow";
            }

            var message = string.IsNullOrWhiteSpace(weeklyReward.Message)
                ? GetTierMessage(weeklyReward.Tier)
                : weeklyReward.Message;

            mailSendService.SendDirectNpcMessageToPlayer(
                sessionId,
                QuartermasterConstants.TraderId.ToString(),
                MessageType.NpcTraderMessage,
                message,
                items,
                172800
            );

            SetLastClaimedWeek(fullProfile, desiredWeek);
            logger.DebugInfo($"[TheQuartermaster] Sent weekly reward {weeklyReward.RewardId} (tier {weeklyReward.Tier}) to {sessionId}.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to claim weekly reward: {ex.Message}", ex);
        }
    }

    private string ResolveTemplateId(int tier)
    {
        if (tier == 4)
        {
            return EnsureTier4Template();
        }

        return TierTemplates.TryGetValue(tier, out var templateId) ? templateId : string.Empty;
    }

    private string EnsureTier4Template()
    {
        var items = databaseService.GetItems();
        if (items.ContainsKey(Tier4TemplateId))
        {
            return Tier4TemplateId;
        }

        if (!items.TryGetValue(TierTemplates[3], out var baseTpl))
        {
            logger.DebugWarning("[TheQuartermaster] Tier 3 supply crate template not found; cannot create Tier 4 clone.");
            return TierTemplates[3];
        }

        var handbook = databaseService.GetHandbook().Items.FirstOrDefault(h => h.Id.ToString() == TierTemplates[3]);
        var details = new NewItemFromCloneDetails
        {
            NewId = Tier4TemplateId,
            ItemTplToClone = new MongoId(TierTemplates[3]),
            ParentId = baseTpl.Parent.ToString(),
            HandbookParentId = handbook is not null ? handbook.ParentId.ToString() : "5b5f6fa186f77409407a7eb7",
            HandbookPriceRoubles = 1,
            FleaPriceRoubles = 1,
            OverrideProperties = new TemplateItemProperties { BackgroundColor = "yellow" },
            Locales = new Dictionary<string, LocaleDetails>
            {
                ["en"] = new LocaleDetails
                {
                    Name = baseTpl.Properties?.Name ?? "Tier 4 Supply Crate",
                    ShortName = baseTpl.Properties?.ShortName ?? "T4 Crate",
                    Description = baseTpl.Properties?.Description ?? "Weekly Tier 4 community supply shipment."
                }
            }
        };

        var result = customItemService.CreateItemFromClone(details);
        if (result.Success != true)
        {
            logger.DebugWarning($"[TheQuartermaster] Tier 4 template clone skipped/failed: {string.Join(", ", result.Errors ?? [])}");
        }

        return Tier4TemplateId;
    }

    private static long GetLastClaimedWeek(SptProfile profile)
    {
        return profile.SptData?.Migrations?.TryGetValue(LastClaimedWeekKey, out var value) == true ? value : 0;
    }

    private static void SetLastClaimedWeek(SptProfile profile, long week)
    {
        profile.SptData ??= new Spt { Migrations = new Dictionary<string, long>() };
        profile.SptData.Migrations ??= new Dictionary<string, long>();
        profile.SptData.Migrations[LastClaimedWeekKey] = week;
    }

    private static long EncodeWeek(string week)
    {
        return long.TryParse(week.Replace("-W", "", StringComparison.OrdinalIgnoreCase), out var value) ? value : 0;
    }

    private static string GetTierMessage(int tier)
    {
        return tier switch
        {
            1 => "Times have been tough, operator. The community's contributions this week were... modest, to put it kindly. I've scraped together what I could from the back of the warehouse. It's not much, but it's what we've got. Take it, and let's hope next week treats us better.",
            2 => "Another week in the books. Business wasn't exactly booming, but we survived. Here's your share of the haul - picked over, but there's still some useful bits in there. Keep your head down out there, and maybe next week we'll have reason to celebrate.",
            3 => "Now this is more like it. The community pulled through this week - solid contributions across the board. I've put together a proper supply crate for you. Quality goods, no scraps here. You've earned this one, operator. Let's keep this momentum going.",
            4 => "Outstanding work, operator. The community absolutely delivered this week - best we've seen in a long time. I've pulled out all the stops on this one. Premium gear, top shelf, the works. This is what happens when everyone does their part. Enjoy it - you've more than earned it.",
            _ => "Weekly community supply shipment. Open the crate to claim your share."
        };
    }
}
