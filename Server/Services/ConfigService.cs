using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class ConfigService(ISptLogger<ConfigService> logger)
{
    public QuartermasterConfig Config { get; private set; } = new();
    public string ModPath { get; private set; } = string.Empty;
    public string ConfigPath { get; private set; } = string.Empty;
    public string SptDataPath { get; private set; } = string.Empty;
    public string SptRootName { get; private set; } = "unknown";

    public void Load(string modPath)
    {
        ModPath = modPath;
        ConfigPath = Path.Combine(modPath, "config");
        SptDataPath = ResolveSptDataPath(modPath);
        SptRootName = ResolveSptRootName(SptDataPath);
        var configPath = Path.Combine(ConfigPath, "config.json");
        if (!File.Exists(configPath))
        {
            logger.Warning($"[TheQuartermaster] Config not found at {configPath}, using defaults.");
            Config = new QuartermasterConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            };
            Config = JsonSerializer.Deserialize<QuartermasterConfig>(json, options) ?? new QuartermasterConfig();
            logger.Info($"[TheQuartermaster] Config loaded from {configPath}");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to load config: {ex.Message}", ex);
            Config = new QuartermasterConfig();
        }
    }

    public bool Debug => Config.DebugLogging;

    private static string ResolveSptDataPath(string modPath)
    {
        // modPath is expected to be SPT/user/mods/TheQuartermaster
        var sptDataPath = Path.GetFullPath(Path.Combine(modPath, "..", "..", "..", "SPT_Data"));
        if (Directory.Exists(sptDataPath))
        {
            return sptDataPath;
        }

        // Fallback: try a sibling "SPT_Data" directory next to the mod's parent
        var modsDir = Path.GetDirectoryName(modPath);
        if (modsDir is not null)
        {
            var userDir = Path.GetDirectoryName(modsDir);
            if (userDir is not null)
            {
                var altPath = Path.Combine(userDir, "SPT_Data");
                if (Directory.Exists(altPath))
                {
                    return altPath;
                }
            }
        }

        return sptDataPath;
    }

    private static string ResolveSptRootName(string sptDataPath)
    {
        if (string.IsNullOrWhiteSpace(sptDataPath))
        {
            return "unknown";
        }

        var parent = Path.GetDirectoryName(sptDataPath);
        return string.IsNullOrWhiteSpace(parent) ? "unknown" : Path.GetFileName(parent) ?? "unknown";
    }
}
