using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class VanillaAllowlistService(
    ISptLogger<VanillaAllowlistService> logger,
    ConfigService configService,
    DatabaseService databaseService
)
{
    private HashSet<string> _vanillaIds = [];
    private Dictionary<string, string>? _vanillaIdToName;

    public void Load(string modPath)
    {
        var path = System.IO.Path.Combine(modPath, "db", "vanilla_items.json");

        if (!File.Exists(path))
        {
            var sptItemsPath = System.IO.Path.Combine(configService.SptDataPath, "database", "templates", "items.json");
            if (File.Exists(sptItemsPath))
            {
                path = sptItemsPath;
            }
        }

        if (!File.Exists(path))
        {
            logger.Warning($"[TheQuartermaster] Vanilla items file not found at {path}, falling back to live database.");
            _vanillaIds = new HashSet<string>(databaseService.GetItems().Keys.Select(x => x.ToString()), StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            _vanillaIds = new HashSet<string>(document.RootElement.EnumerateObject().Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            _vanillaIdToName = [];
            foreach (var prop in document.RootElement.EnumerateObject())
            {
                var name = prop.Value.TryGetProperty("_name", out var nameElement) ? nameElement.GetString() : null;
                _vanillaIdToName[prop.Name] = name ?? prop.Name;
            }

            logger.Info($"[TheQuartermaster] Loaded {_vanillaIds.Count} vanilla item IDs from {path}");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to load vanilla allowlist: {ex.Message}", ex);
            _vanillaIds = [];
        }
    }

    public bool IsVanilla(MongoId tpl) => _vanillaIds.Contains(tpl.ToString());

    public bool IsVanilla(string? tpl) => !string.IsNullOrWhiteSpace(tpl) && _vanillaIds.Contains(tpl);

    public bool AllVanilla(IEnumerable<MongoId> tpls) => tpls.All(t => IsVanilla(t));

    public bool IsTemplateInRuntimeDatabase(MongoId tpl) => databaseService.GetItems().ContainsKey(tpl);

    public bool AllTemplatesInRuntimeDatabase(IEnumerable<MongoId> tpls) => tpls.All(t => IsTemplateInRuntimeDatabase(t));

    public string? GetVanillaName(MongoId tpl)
    {
        if (_vanillaIdToName is null) return null;
        _vanillaIdToName.TryGetValue(tpl.ToString(), out var name);
        return name;
    }
}
