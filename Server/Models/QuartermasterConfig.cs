using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models;

public record QuartermasterConfig
{
    [JsonPropertyName("modEnabled")]
    public bool ModEnabled { get; set; } = true;

    [JsonPropertyName("uploadConsent")]
    public bool UploadConsent { get; set; } = true;

    [JsonPropertyName("baseMarkupPercent")]
    public double BaseMarkupPercent { get; set; } = 15.0;

    [JsonPropertyName("vanillaItemsOnly")]
    public bool VanillaItemsOnly { get; set; } = false;
}
