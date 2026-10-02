using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class LatestReleaseResponse
{
    [JsonPropertyName("success")]
    [JsonRequired]
    public bool Success { get; set; }

    [JsonPropertyName("generated_at")]
    public string? GeneratedAt { get; set; }

    [JsonPropertyName("data")]
    [JsonRequired]
    public LatestReleaseData? Data { get; set; }
}

public sealed class LatestReleaseData
{
    [JsonPropertyName("official_patch")]
    [JsonRequired]
    public int OfficialPatch { get; set; }

    [JsonPropertyName("filename")]
    [JsonRequired]
    public string? Filename { get; set; }

    [JsonPropertyName("install_guide_url")]
    [JsonRequired]
    public string? InstallGuideUrl { get; set; }

    [JsonPropertyName("mode")]
    [JsonRequired]
    public LatestReleaseMode? Mode { get; set; }

    [JsonPropertyName("current")]
    [JsonRequired]
    public CurrentRelease? Current { get; set; }
}

public sealed class LatestReleaseMode
{
    [JsonPropertyName("slug")]
    [JsonRequired]
    public string? Slug { get; set; }

    [JsonPropertyName("public_name")]
    [JsonRequired]
    public string? PublicName { get; set; }
}

public sealed class LatestReleaseErrorResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("allowed")]
    public List<string>? Allowed { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
