using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TheQuartermaster.Client.UI;
using UnityEngine;

namespace TheQuartermaster.Client.Patches
{
    internal static class CommunityTraderTabPatcher
    {
        private static bool _hasLoggedDiscovery;

        public static void Enable()
        {
            var harmony = new Harmony("com.shaneeexd.thequartermaster.communitytab");
            var postfix = new HarmonyMethod(typeof(CommunityTraderTabPatcher).GetMethod("Postfix"));

            string[] typeNames = { "EFT.UI.TraderScreensGroup", "EFT.UI.TraderScreen", "EFT.UI.TradeScreen" };
            var patched = new HashSet<MethodInfo>();

            foreach (var typeName in typeNames)
            {
                var type = AccessTools.TypeByName(typeName);
                if (type == null)
                {
                    Plugin.Log.LogWarning($"[TheQuartermaster] Could not find trader UI type {typeName}.");
                    continue;
                }

                var showMethods = AccessTools.GetDeclaredMethods(type)
                    .Where(m => m.Name == "Show")
                    .OrderByDescending(m => m.GetParameters().Length);

                foreach (var method in showMethods)
                {
                    if (patched.Contains(method))
                        continue;
                    try
                    {
                        harmony.Patch(method, postfix: postfix);
                        patched.Add(method);
                        Plugin.Log.LogInfo($"[TheQuartermaster] Patched {type.Name}.{method} for Community tab.");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[TheQuartermaster] Could not patch {type.Name}.Show: {ex.Message}");
                    }
                }
            }

            if (patched.Count == 0)
                DiscoverTraderUiTypes();
        }

        public static void Postfix(object __instance)
        {
            try
            {
                if (CommunityPanel.Instance == null)
                {
                    Plugin.Log.LogWarning("[TheQuartermaster] CommunityPanel.Instance is null, cannot attach tab.");
                    return;
                }

                var traderScreen = __instance as Component;
                if (traderScreen == null)
                    return;

                CommunityPanel.Instance.CurrentTraderScreen = traderScreen;
                Plugin.Log.LogInfo($"[TheQuartermaster] CommunityTraderTabPatcher set trader screen: {traderScreen.GetType().Name}");
                CommunityPanel.Instance.RefreshCommunityTab();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[TheQuartermaster] CommunityTraderTabPatcher.Postfix error: {ex.Message}");
            }
        }

        private static bool IsQuartermasterTrader(object instance)
        {
            try
            {
                var traverse = Traverse.Create(instance);
                var trader = traverse.Property("Trader")?.GetValue()
                             ?? traverse.Field("_trader")?.GetValue()
                             ?? traverse.Property("SelectedTrader")?.GetValue();

                if (trader == null)
                {
                    Plugin.Log.LogWarning("[TheQuartermaster] Could not find Trader/SelectedTrader on trader screen instance.");
                    return false;
                }

                var traderId = Traverse.Create(trader).Property("Id")?.GetValue()?.ToString()
                               ?? Traverse.Create(trader).Property("ProfileId")?.GetValue()?.ToString()
                               ?? Traverse.Create(trader).Field("_id")?.GetValue()?.ToString();

                if (!string.IsNullOrWhiteSpace(traderId))
                    return traderId.IndexOf("quartermaster", StringComparison.OrdinalIgnoreCase) >= 0
                           || traderId.IndexOf("Quartermaster", StringComparison.OrdinalIgnoreCase) >= 0;

                var nickname = Traverse.Create(trader).Property("Nickname")?.GetValue()?.ToString()
                               ?? Traverse.Create(trader).Property("LocalizedName")?.GetValue()?.ToString();
                if (!string.IsNullOrWhiteSpace(nickname))
                    return nickname.IndexOf("quartermaster", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                // ignored
            }

            return false;
        }

        private static void DiscoverTraderUiTypes()
        {
            if (_hasLoggedDiscovery)
                return;
            _hasLoggedDiscovery = true;

            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .Where(a => a.GetName().Name == "Assembly-CSharp" || (a.FullName?.StartsWith("Assembly-CSharp") ?? false));

                var traderTypes = new List<Type>();
                foreach (var asm in assemblies)
                {
                    try
                    {
                        traderTypes.AddRange(asm.GetTypes()
                            .Where(t => t.Namespace == "EFT.UI" && t.Name.IndexOf("Trader", StringComparison.OrdinalIgnoreCase) >= 0));
                    }
                    catch { /* ignore reflection errors */ }
                }
                traderTypes = traderTypes.OrderBy(t => t.Name).ToList();

                Plugin.Log.LogWarning("[TheQuartermaster] Could not find a known trader Show method. Discovered EFT.UI types: "
                    + string.Join(", ", traderTypes.Select(t => t.Name).Take(20)));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[TheQuartermaster] Failed to discover trader UI types: {ex.Message}");
            }
        }
    }
}
