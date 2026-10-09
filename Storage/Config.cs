using System.Text.Json.Serialization;

namespace BdoClient.Storage;

public sealed class Config
{
    [JsonPropertyName("game_path")]
    public string? GamePath { get; set; }

    [JsonPropertyName("last_mode")]
    public string? LastMode { get; set; }

    [JsonPropertyName("last_mode_variant")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastModeVariant { get; set; }

}
