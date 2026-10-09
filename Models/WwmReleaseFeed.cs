using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class WwmReleaseFeed
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("generated_at")] public string? GeneratedAt { get; set; }
    [JsonPropertyName("data")] public WwmReleaseData? Data { get; set; }
}

public sealed class WwmReleaseData
{
    [JsonPropertyName("current")] public WwmCurrentRelease? Current { get; set; }
    [JsonPropertyName("modes")] public List<WwmMode>? Modes { get; set; }
}

public sealed class WwmCurrentRelease
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("published_at")] public string? PublishedAt { get; set; }
    [JsonPropertyName("game_version")] public string? GameVersion { get; set; }
    [JsonPropertyName("game_tested")] public bool GameTested { get; set; }
    [JsonPropertyName("translated_rows")] public long? TranslatedRows { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("files")] public List<WwmReleaseFile>? Files { get; set; }
}

public sealed class WwmReleaseFile
{
    [JsonPropertyName("variant")] public string? Variant { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("download_url")] public string? DownloadUrl { get; set; }
    [JsonPropertyName("size_bytes")] public long? SizeBytes { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
}

public sealed class WwmMode
{
    [JsonPropertyName("variant")] public string? Variant { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("audience")] public string? Audience { get; set; }
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("download_url")] public string? DownloadUrl { get; set; }
    [JsonPropertyName("size_bytes")] public long? SizeBytes { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
}
