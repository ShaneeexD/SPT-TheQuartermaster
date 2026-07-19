using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

using TheQuartermaster.Client.Patches;
using TheQuartermaster.Client.Services;
using TheQuartermaster.Client.UI;
using UnityEngine;

namespace TheQuartermaster.Client
{
    [BepInPlugin("com.shaneeexd.thequartermaster", "The Quartermaster Client", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static bool DebugLogging;
        internal static ConfigEntry<bool> ShowScavengedTags;
        internal static ConfigEntry<bool> ShowSellerTags;

        private void Awake()
        {
            Log = Logger;

            var debugConfig = Config.Bind("General", "DebugLogging", false, "Enable debug logging for the client mod");
            DebugLogging = debugConfig.Value;

            ShowScavengedTags = Config.Bind("Tags", "ShowScavengedTags", true, "Show red skull tags on scavenged marketplace items");
            ShowSellerTags = Config.Bind("Tags", "ShowSellerTags", false, "Show green seller name tags on sold marketplace items");

            if (DebugLogging)
                Log.LogInfo("The Quartermaster Client loading...");

            try
            {
                new QuestExpiryCountdownPatch().Enable();
                new TagComponentInjectionPatch().Enable();
                new ScavengedTagPatch().Enable();

                var pluginFolder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                CommunityApiClient.Init(Config, pluginFolder);

                var panelObject = new GameObject("CommunityPanel");
                panelObject.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(panelObject);
                panelObject.AddComponent<CommunityPanel>();

                try
                {
                    CommunityTraderTabPatcher.Enable();
                    if (DebugLogging)
                        Log.LogInfo("[TheQuartermaster] Community trader tab patcher enabled.");
                }
                catch (Exception tabEx)
                {
                    Log.LogWarning($"[TheQuartermaster] Community tab patcher could not be enabled; use F9 to open the panel: {tabEx.Message}");
                }

                if (DebugLogging)
                    Log.LogInfo("The Quartermaster Client loaded successfully!");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to load: {ex}");
            }
        }
    }
}
