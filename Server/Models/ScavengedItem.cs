using System.Text.Json.Serialization;

namespace TheQuartermaster.Server.Models;

public class ScavengedItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("item_tree")]
    public string? ItemTreeJson { get; set; }

    [JsonPropertyName("root_tpl")]
    public string? RootTpl { get; set; }

    [JsonPropertyName("root_name")]
    public string? RootName { get; set; }

    [JsonPropertyName("original_owner_name")]
    public string? OriginalOwnerName { get; set; }

    [JsonPropertyName("available_at")]
    public long AvailableAt { get; set; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("server_id")]
    public string? ServerId { get; set; }
}
