using Google.Cloud.Firestore;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class ItemOverrideService(
    ISptLogger<ItemOverrideService> logger,
    FirestoreService firestoreService
)
{
    private const string CollectionName = "quartermaster_item_overrides";

    private readonly Dictionary<string, ItemPriceOverride> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

    public bool IsEnabled => firestoreService.IsEnabled;

    public async Task RefreshAsync()
    {
        if (!firestoreService.IsEnabled)
        {
            return;
        }

        lock (_lock)
        {
            if (DateTime.UtcNow - _lastRefresh < _cacheTtl)
            {
                return;
            }
        }

        try
        {
            var snapshot = await firestoreService.Db!.Collection(CollectionName)
                .WhereEqualTo("enabled", true)
                .GetSnapshotAsync();

            var overrides = new Dictionary<string, ItemPriceOverride>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in snapshot.Documents)
            {
                var ovr = doc.ConvertTo<ItemPriceOverride>();
                if (string.IsNullOrWhiteSpace(ovr.Tpl))
                {
                    continue;
                }

                overrides[ovr.Tpl] = ovr;
            }

            lock (_lock)
            {
                _overrides.Clear();
                foreach (var kvp in overrides)
                {
                    _overrides[kvp.Key] = kvp.Value;
                }

                _lastRefresh = DateTime.UtcNow;
            }

            logger.Info($"[TheQuartermaster] Loaded {overrides.Count} item price override(s) from Firestore.");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Failed to load item price overrides: {ex.Message}", ex);
        }
    }

    public bool TryGetPrice(string tpl, out long price, out string currencyTpl)
    {
        lock (_lock)
        {
            if (_overrides.TryGetValue(tpl, out var ovr) && ovr.Enabled)
            {
                price = ovr.Price;
                currencyTpl = ResolveCurrencyTpl(ovr.Currency);
                return true;
            }
        }

        price = 0;
        currencyTpl = ResolveCurrencyTpl("RUB");
        return false;
    }

    private static string ResolveCurrencyTpl(string? currency)
    {
        var upper = currency?.ToUpperInvariant() ?? "RUB";
        return upper switch
        {
            "USD" or "DOLLARS" or "DOLLAR" => "5696686a4bdc2da3298b456a",
            "EUR" or "EUROS" or "EURO" => "569668774bdc2da2298b4568",
            _ => "5449016a4bdc2d6f028b456f" // RUB
        };
    }
}
