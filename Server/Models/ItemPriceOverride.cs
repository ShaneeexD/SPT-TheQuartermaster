using Google.Cloud.Firestore;
using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models;

[FirestoreData]
public class ItemPriceOverride
{
    [FirestoreProperty("tpl")]
    [JsonPropertyName("tpl")]
    public string Tpl { get; set; } = string.Empty;

    [FirestoreProperty("price")]
    [JsonPropertyName("price")]
    public long Price { get; set; }

    [FirestoreProperty("currency")]
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "RUB";

    [FirestoreProperty("enabled")]
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
