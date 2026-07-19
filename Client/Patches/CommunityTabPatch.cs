using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;
using TheQuartermaster.Client.UI;

namespace TheQuartermaster.Client.Patches
{
    public class CommunityTabPatch : ModulePatch
    {
        private static Type _tabBarType;
        private static bool _hasLoggedDiscovery;

        protected override MethodBase GetTargetMethod()
        {
            try
            {
                _tabBarType = AccessTools.TypeByName("EFT.UI.TraderScreensGroup")
                              ?? AccessTools.TypeByName("EFT.UI.TraderScreen")
                              ?? AccessTools.TypeByName("EFT.UI.TradeScreen");

                if (_tabBarType == null)
                {
                    DiscoverTraderUiTypes();
                    return null;
                }

                var method = AccessTools.GetDeclaredMethods(_tabBarType)
                    .FirstOrDefault(m => m.Name == "Show");

                if (method == null)
                {
                    if (Plugin.DebugLogging)
                        Plugin.Log.LogDebug($"[TheQuartermaster] {_tabBarType.Name} has no Show method.");
                    return null;
                }

                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug($"[TheQuartermaster] Patching {_tabBarType.FullName}.{method.Name}.");

                return method;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[TheQuartermaster] CommunityTabPatch discovery failed: {ex.Message}");
                return null;
            }
        }

        [PatchPostfix]
        public static void Postfix(object __instance)
        {
            try
            {
                if (CommunityPanel.Instance == null)
                    return;

                // Try to identify if the currently opened trader is The Quartermaster.
                bool isQuartermaster = IsQuartermasterTrader(__instance);
                if (isQuartermaster)
                {
                    CommunityPanel.Instance.Visible = true;
                }
            }
            catch (Exception ex)
            {
                if (Plugin.DebugLogging)
                    Plugin.Log.LogDebug($"[TheQuartermaster] CommunityTabPatch postfix error: {ex.Message}");
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
                    return false;

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

                var traderTypes = new System.Collections.Generic.List<Type>();
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

                Plugin.Log.LogWarning("[TheQuartermaster] Could not find a known trader tab bar type. Discovered EFT.UI types: "
                    + string.Join(", ", traderTypes.Select(t => t.Name).Take(20)));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[TheQuartermaster] Failed to discover trader UI types: {ex.Message}");
            }
        }
    }
}
