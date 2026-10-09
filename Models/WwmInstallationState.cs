using System.Text.Json.Serialization;

namespace BdoClient.Models;

public sealed class WwmInstallationState
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("game_id")] public string GameId { get; set; } = "where-winds-meet";
    [JsonPropertyName("mode_slug")] public string? ModeSlug { get; set; }
    [JsonPropertyName("mode_variant")] public string? ModeVariant { get; set; }
    [JsonPropertyName("release_version")] public string? ReleaseVersion { get; set; }
    [JsonPropertyName("archive_size_bytes")] public long ArchiveSizeBytes { get; set; }
    [JsonPropertyName("archive_sha256")] public string? ArchiveSha256 { get; set; }
    [JsonPropertyName("pre_hub_snapshot_cycle_id")] public string PreHubSnapshotCycleId { get; set; } = "";
    [JsonPropertyName("game_build_id")] public string? GameBuildId { get; set; }
    [JsonPropertyName("installed_at")] public DateTimeOffset InstalledAt { get; set; }
    [JsonPropertyName("targets")] public List<WwmTargetManifestEntry> Targets { get; set; } = new();
}

public sealed class WwmTargetManifestEntry
{
    [JsonPropertyName("relative_path")] public string RelativePath { get; set; } = "";
    [JsonPropertyName("size_bytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}

public sealed class WwmApiPackage
{
    public required WwmMode Mode { get; init; }
    public required string Version { get; init; }
    public required string DownloadUrl { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
}

public enum WwmInstalledStateKind { Unknown, Current, UpdateAvailable, Modified }

public sealed record WwmInstalledStateResult(WwmInstalledStateKind State, string? Message = null);
