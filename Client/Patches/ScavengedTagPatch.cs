using System;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.UI;

namespace TheQuartermaster.Client.Patches;

public class ScavengedTagPatch : ModulePatch
{
    private static readonly FieldInfo TagColorField = AccessTools.Field(typeof(GridItemView), "_tagColor");

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

            if (!tagComponent.Name.StartsWith("Scavenged from:", StringComparison.OrdinalIgnoreCase))
                return;

            var tagColor = TagColorField.GetValue(__instance) as Image;
            if (tagColor == null || !tagColor.gameObject.activeSelf)
                return;

            var rt = tagColor.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(0f, 0f);

            if (Plugin.DebugLogging)
                Plugin.Log.LogDebug($"[TheQuartermaster] Repositioned scavenged tag to bottom-left on {item.TemplateId} ({item.Id})");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[TheQuartermaster] ScavengedTagPatch error: {ex}");
        }
    }
}
