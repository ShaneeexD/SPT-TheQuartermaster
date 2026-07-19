using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using TheQuartermaster.Client.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheQuartermaster.Client.Patches;

public class ScavengedTagPatch : ModulePatch
{
    private static readonly FieldInfo TagColorField = AccessTools.Field(typeof(GridItemView), "_tagColor");
    private static readonly FieldInfo TagNameField = AccessTools.Field(typeof(GridItemView), "TagName");
    private static Sprite _skullSprite;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(GridItemView), "method_21");
    }

    [PatchPostfix]
    public static void Postfix(GridItemView __instance)
    {
        try
        {
            if (__instance == null)
                return;

            if (!(__instance is TradingItemView))
                return;

            var item = __instance.Item;
            if (item == null)
                return;

            var tagComponent = item.GetItemComponent<TagComponent>();
            if (tagComponent == null || string.IsNullOrEmpty(tagComponent.Name))
                return;

            if (Plugin.DebugLogging)
                Plugin.Log.LogDebug($"[TheQuartermaster] Tag name='{tagComponent.Name}' len={tagComponent.Name.Length} firstChar=U+{((int)tagComponent.Name[0]):X4} color={tagComponent.Color}");

            var tagColor = TagColorField.GetValue(__instance) as Image;
            if (tagColor == null || !tagColor.gameObject.activeSelf)
                return;

            var tagName = TagNameField?.GetValue(__instance) as TextMeshProUGUI;

            // Reposition to bottom-left and render on top of the item icon
            var rt = tagColor.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(0f, 0f);

            // Move the tag to the end of the sibling list so it renders on top
            tagColor.transform.SetAsLastSibling();

            // Scavenged tag: red skull
            if (tagComponent.Name.StartsWith("| "))
            {
                if (Plugin.ShowScavengedTags != null && !Plugin.ShowScavengedTags.Value)
                {
                    HideTag(tagColor, tagName);
                    return;
                }

                tagColor.gameObject.SetActive(true);
                tagColor.color = new Color32(180, 0, 0, 255);
                AddSkullIcon(tagColor.gameObject);

                if (tagName != null)
                {
                    tagName.gameObject.SetActive(true);
                    tagName.text = "   " + tagComponent.Name.Substring(2);
                }

                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug($"[TheQuartermaster] Repositioned scavenged tag to bottom-left on {item.TemplateId} ({item.Id})");
                return;
            }

            // Seller tag: green
            if (tagComponent.Name.StartsWith("> "))
            {
                if (Plugin.ShowSellerTags != null && !Plugin.ShowSellerTags.Value)
                {
                    HideTag(tagColor, tagName);
                    return;
                }

                tagColor.gameObject.SetActive(true);
                tagColor.color = new Color32(0, 180, 0, 255);

                if (tagName != null)
                {
                    tagName.gameObject.SetActive(true);
                    tagName.alignment = TextAlignmentOptions.Left;
                    tagName.text = tagComponent.Name.Substring(2).TrimStart();
                }

                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug($"[TheQuartermaster] Rendered seller tag to bottom-left on {item.TemplateId} ({item.Id})");
                return;
            }
        }
        catch (Exception ex)
        {
            if (Plugin.DebugLogging) Plugin.Log.LogError($"[TheQuartermaster] ScavengedTagPatch error: {ex}");
        }
    }

    private static void HideTag(Image tagColor, TextMeshProUGUI tagName)
    {
        tagColor.gameObject.SetActive(false);
        if (tagName != null)
            tagName.gameObject.SetActive(false);
    }

    private static void AddSkullIcon(GameObject tagBg)
    {
        const string skullGoName = "QM_SkullIcon";

        // Already added
        if (tagBg.transform.Find(skullGoName) != null)
            return;

        // Load the skull sprite from StaticIcons
        if (_skullSprite == null)
        {
            var staticIcons = EFTHardSettings.Instance?.StaticIcons;
            if (staticIcons == null)
            {
                if (Plugin.DebugLogging) Plugin.Log.LogWarning("[TheQuartermaster] StaticIcons not available, skipping skull icon");
                return;
            }

            _skullSprite = staticIcons.QuestTypeSprites[RawQuestClass.EQuestType.Elimination];
            if (_skullSprite == null)
            {
                if (Plugin.DebugLogging) Plugin.Log.LogWarning("[TheQuartermaster] Elimination skull sprite not found in StaticIcons");
                return;
            }
        }

        // Create skull icon Image to the left of the text
        var skullGo = new GameObject(skullGoName);
        skullGo.transform.SetParent(tagBg.transform, false);
        var skullRt = skullGo.AddComponent<RectTransform>();
        skullRt.anchorMin = new Vector2(0f, 0.5f);
        skullRt.anchorMax = new Vector2(0f, 0.5f);
        skullRt.pivot = new Vector2(0f, 0.5f);
        skullRt.anchoredPosition = new Vector2(2f, 0f);
        skullRt.sizeDelta = new Vector2(12f, 12f);

        var skullImage = skullGo.AddComponent<Image>();
        skullImage.sprite = _skullSprite;
        skullImage.color = Color.white;
        skullImage.raycastTarget = false;
        skullImage.preserveAspect = true;
    }

}
