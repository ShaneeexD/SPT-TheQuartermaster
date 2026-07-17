using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using TheQuartermaster.Client.Patches;

namespace TheQuartermaster.Client
{
    [BepInPlugin("com.shaneeexd.thequartermaster", "The Quartermaster Client", "1.0.7")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static bool DebugLogging;

        private void Awake()
        {
            Log = Logger;

            var debugConfig = Config.Bind("General", "DebugLogging", false, "Enable debug logging for the client mod");
            DebugLogging = debugConfig.Value;

            if (DebugLogging)
                Log.LogInfo("The Quartermaster Client loading...");

            try
            {
                new QuestExpiryCountdownPatch().Enable();

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
