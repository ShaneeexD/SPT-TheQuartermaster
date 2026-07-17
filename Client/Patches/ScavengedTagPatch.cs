using System;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheQuartermaster.Client.Patches;

public class ScavengedTagPatch : ModulePatch
{
    private const string TagGoName = "QM_ScavengedTag";

    private static readonly FieldInfo TagColorField = AccessTools.Field(typeof(GridItemView), "_tagColor");
    private static readonly FieldInfo TagNameField = AccessTools.Field(typeof(GridItemView), "TagName");

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(GridItemView), "method_21");
    }

    [PatchPrefix]
    public static void Prefix(GridItemView __instance)
    {
        try
        {
            if (Plugin.DebugLogging)
                Plugin.Log.LogDebug("[TheQuartermaster] ScavengedTagPatch Prefix called");

            if (__instance == null)
            {
                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug("[TheQuartermaster] __instance is null");
                return;
            }

            var instanceType = __instance.GetType();
            if (Plugin.DebugLogging)
                Plugin.Log.LogDebug($"[TheQuartermaster] GridItemView type: {instanceType.FullName}");

            // Only apply to trader assortment items
            if (!(__instance is TradingItemView))
            {
                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug($"[TheQuartermaster] Not a TradingItemView, skipping ({instanceType.FullName})");
                return;
            }

            var item = __instance.Item;
            if (item == null)
            {
                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug("[TheQuartermaster] __instance.Item is null");
                return;
            }

            if (Plugin.DebugLogging)
                Plugin.Log.LogDebug($"[TheQuartermaster] Item: {item.TemplateId} ({item.Id})");

            var tagComponent = item.GetItemComponent<TagComponent>();
            if (tagComponent == null)
            {
                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug("[TheQuartermaster] TagComponent is null");
                return;
            }

            if (Plugin.DebugLogging)
                Plugin.Log.LogDebug($"[TheQuartermaster] TagComponent: Name='{tagComponent.Name}', Color={tagComponent.Color}");

            if (string.IsNullOrEmpty(tagComponent.Name))
                return;

            // Only handle our scavenged tags
            if (!tagComponent.Name.StartsWith("Scavenged from:", StringComparison.OrdinalIgnoreCase))
            {
                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug($"[TheQuartermaster] Tag is not scavenged, skipping: '{tagComponent.Name}'");
                return;
            }

            var rectTransform = __instance.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug("[TheQuartermaster] RectTransform is null");
                return;
            }

            // If the trading layout already has the tag UI elements, let the original method handle it
            var existingTagColor = TagColorField.GetValue(__instance) as Image;
            var existingTagName = TagNameField.GetValue(__instance) as TextMeshProUGUI;
            if (existingTagColor != null && existingTagName != null)
            {
                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug("[TheQuartermaster] Existing tag UI elements present, letting original render");
                return;
            }

            if (Plugin.DebugLogging)
                Plugin.Log.LogDebug("[TheQuartermaster] Creating/assigning scavenged tag UI");

            // Check if we already created the tag overlay
            var existing = rectTransform.Find(TagGoName);
            GameObject tagGo;
            Image tagBg;
            TextMeshProUGUI tagText;

            if (existing != null)
            {
                tagGo = existing.gameObject;
                tagBg = tagGo.transform.Find("TagBg")?.GetComponent<Image>();
                tagText = tagGo.GetComponentInChildren<TextMeshProUGUI>();
            }
            else
            {
                // Create the tag container at bottom-left
                tagGo = new GameObject(TagGoName);
                tagGo.transform.SetParent(rectTransform, false);
                var tagRt = tagGo.AddComponent<RectTransform>();
                tagRt.anchorMin = new Vector2(0f, 0f);
                tagRt.anchorMax = new Vector2(0f, 0f);
                tagRt.pivot = new Vector2(0f, 0f);
                tagRt.anchoredPosition = new Vector2(0f, 0f);

                // Background image (red)
                var bgGo = new GameObject("TagBg");
                bgGo.transform.SetParent(tagGo.transform, false);
                var bgRt = bgGo.AddComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = Vector2.zero;
                bgRt.offsetMax = Vector2.zero;
                tagBg = bgGo.AddComponent<Image>();
                tagBg.color = new Color(0.6f, 0f, 0f, 0.85f);

                // Text
                var textGo = new GameObject("TagText");
                textGo.transform.SetParent(tagGo.transform, false);
                var textRt = textGo.AddComponent<RectTransform>();
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = new Vector2(3f, 1f);
                textRt.offsetMax = new Vector2(-3f, -1f);
                tagText = textGo.AddComponent<TextMeshProUGUI>();
                tagText.fontSize = 10f;
                tagText.alignment = TextAlignmentOptions.Left;
                tagText.color = Color.white;
                tagText.raycastTarget = false;
                tagText.enableWordWrapping = false;
                tagText.overflowMode = TextOverflowModes.Ellipsis;

                // Assign the game's built-in fields so method_21 renders it normally
                TagColorField.SetValue(__instance, tagBg);
                TagNameField.SetValue(__instance, tagText);
            }

            if (tagBg != null && tagText != null)
            {
                tagText.text = tagComponent.Name;
                tagBg.color = EditTagWindow.GetColor(tagComponent.Color);

                // Cap tag width to the item width, truncate with ellipsis if needed
                var itemWidth = rectTransform.sizeDelta.x;
                tagText.ForceMeshUpdate();
                var textWidth = tagText.preferredWidth;
                var tagWidth = Mathf.Min(textWidth + 8f, itemWidth);
                tagGo.GetComponent<RectTransform>().sizeDelta = new Vector2(tagWidth, 16f);
                tagGo.SetActive(true);

                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug("[TheQuartermaster] Scavenged tag UI created/reused successfully");
            }
            else if (Plugin.DebugLogging)
            {
                Plugin.Log.LogDebug("[TheQuartermaster] tagBg or tagText is null after setup");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[TheQuartermaster] ScavengedTagPatch error: {ex}");
        }
    }
}
