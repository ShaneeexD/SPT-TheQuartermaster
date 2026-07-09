using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models;

public record QuartermasterConfig
{
    [JsonPropertyName("modEnabled")]
    public bool ModEnabled { get; set; } = true;

    [JsonPropertyName("uploadConsent")]
    public bool UploadConsent { get; set; } = true;

    // Community contracts (client-side display toggles only)
    [JsonPropertyName("allowCommunityContracts")]
    public bool AllowCommunityContracts { get; set; } = true;

    [JsonPropertyName("allowAdminContracts")]
    public bool AllowAdminContracts { get; set; } = true;
}
