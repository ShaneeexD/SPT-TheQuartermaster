using System;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Reflection.Patching;
using EFT;

namespace TheQuartermaster.Client.Patches;

public class TagComponentInjectionPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ItemFactory), nameof(ItemFactory.CreateItem));
    }

    [PatchPostfix]
    public static void Postfix(Item __result, UnparsedData itemDiff)
    {
        if (__result == null || itemDiff == null || itemDiff.JToken == null)
            return;

        if (__result.GetItemComponent<TagComponent>() != null)
            return;

        var token = itemDiff.JToken as JObject;
        if (token == null)
            return;

        var tagToken = token["Tag"] ?? token["tag"];
        if (tagToken == null)
            return;

        try
        {
            var name = tagToken["Name"]?.Value<string>() ?? tagToken["name"]?.Value<string>() ?? string.Empty;
            var color = tagToken["Color"]?.Value<int>() ?? tagToken["color"]?.Value<int>() ?? 0;

            var tagComponent = new TagComponent(__result);
            tagComponent.Name = name;
            tagComponent.Color = color;
            __result.Components.Add(tagComponent);

            if (Plugin.DebugLogging)
                Plugin.Log.LogDebug($"[TheQuartermaster] Injected TagComponent on {__result.TemplateId} ({__result.Id}): Name='{name}', Color={color}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[TheQuartermaster] TagComponentInjectionPatch error: {ex}");
        }
    }
}
