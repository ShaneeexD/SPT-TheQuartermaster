using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using EFT;

namespace YourModName.Client
{
    [BepInPlugin("com.yourname.yourmod.client", "Your Mod Name Client", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        
        // Define your custom bot types here
        // Use high enum values to avoid conflicts (900+)
        private static readonly Dictionary<string, WildSpawnType> CustomBotTypes = new Dictionary<string, WildSpawnType>
        {
            { "bossYourBoss", (WildSpawnType)900 },
            { "followerYourBoss", (WildSpawnType)901 }
        };

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("YourMod Client loading...");
            
            try
            {
                var harmony = new Harmony("com.yourname.yourmod.client");
                
                // Add custom bot types to game dictionaries
                AddCustomBotTypes();
                
                // Patch Enum.Parse to handle custom WildSpawnType values
                PatchEnumParse(harmony);
                
                // Apply all Harmony patches in this assembly
                harmony.PatchAll();
                
                Log.LogInfo("YourMod Client loaded successfully!");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to load: {ex}");
            }
        }
        
        private void AddCustomBotTypes()
        {
            try
            {
                // Add to BotSettingsRepoClass.Dictionary_0
                var dictionary0Field = typeof(BotSettingsRepoClass).GetField("Dictionary_0", BindingFlags.Public | BindingFlags.Static);
                if (dictionary0Field != null)
                {
                    var dictionary = dictionary0Field.GetValue(null) as System.Collections.IDictionary;
                    if (dictionary != null)
                    {
                        var gclass790Type = dictionary0Field.FieldType.GetGenericArguments()[1];
                        
                        foreach (var botType in CustomBotTypes)
                        {
                            if (!dictionary.Contains(botType.Value))
                            {
                                bool isBoss = botType.Key.StartsWith("boss");
                                var settings = Activator.CreateInstance(gclass790Type, isBoss, !isBoss, false, 
                                    isBoss ? "ScavRole/Boss" : "ScavRole/Follower", (ETagStatus)0);
                                dictionary.Add(botType.Value, settings);
                                Log.LogInfo($"Added bot type: {botType.Key}");
                            }
                        }
                    }
                }
                
                // Add to ExcludedDifficulties
                var excludedField = typeof(GClass598).GetField("ExcludedDifficulties", BindingFlags.Static | BindingFlags.Public);
                if (excludedField != null)
                {
                    var excluded = (Dictionary<WildSpawnType, List<BotDifficulty>>)excludedField.GetValue(null);
                    var difficulties = new List<BotDifficulty> { BotDifficulty.easy, BotDifficulty.hard, BotDifficulty.impossible };
                    
                    foreach (var botType in CustomBotTypes)
                    {
                        if (!excluded.ContainsKey(botType.Value))
                        {
                            excluded.Add(botType.Value, difficulties);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Error adding custom bot types: {ex}");
            }
        }
        
        private void PatchEnumParse(Harmony harmony)
        {
            var parseMethod = typeof(Enum).GetMethod("Parse", new Type[] { typeof(Type), typeof(string) });
            if (parseMethod != null)
            {
                harmony.Patch(parseMethod, prefix: new HarmonyMethod(typeof(EnumParsePatch).GetMethod("Prefix")));
                Log.LogInfo("Patched Enum.Parse for custom bot types");
            }
        }
        
        public static class EnumParsePatch
        {
            public static bool Prefix(Type enumType, string value, ref object __result)
            {
                if (string.IsNullOrEmpty(value)) return true;
                
                if (enumType == typeof(WildSpawnType) && CustomBotTypes.ContainsKey(value))
                {
                    __result = CustomBotTypes[value];
                    return false;
                }
                return true;
            }
        }
    }
}
