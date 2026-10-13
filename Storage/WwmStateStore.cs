using System.Text.Json;
using System.Text.Json.Serialization;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Storage;

public sealed class WwmStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly GamePersistencePaths _paths;
    public string StateFile => Path.Combine(_paths.StateDir, "wwm-installation.json");
    public string SnapshotDirectory => Path.Combine(_paths.BackupsDir, "pre-hub");
    public string TransactionDirectory => Path.Combine(_paths.StateDir, "wwm-transaction");
    public string JournalFile => Path.Combine(TransactionDirectory, "journal.json");

    public WwmStateStore(GamePersistencePaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public WwmInstallationState? Load(out string? error)
    {
        error = null;
        if (!File.Exists(StateFile)) return null;
        try
        {
            return TryDeserializeState(File.ReadAllText(StateFile), out var value, out error) ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return null;
        }
    }

    public async Task SaveAsync(WwmInstallationState value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsValidState(value))
            throw new InvalidDataException("Refusing to persist invalid WWM state.");
        await WriteAtomicAsync(StateFile, SerializeState(value), cancellationToken).ConfigureAwait(false);
    }

    public static string SerializeState(WwmInstallationState value) => JsonSerializer.Serialize(value, Options);

    internal static bool TryDeserializeState(string json, out WwmInstallationState? value, out string? error)
    {
        value = null;
        error = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<WwmInstallationState>(json, Options);
            if (!IsValidState(parsed))
            {
                error = "WWM installation state failed validation.";
                return false;
            }
            value = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsValidState(WwmInstallationState? value)
        => value != null && value.SchemaVersion is 1 or 2 && value.GameId == "where-winds-meet"
            && !string.IsNullOrWhiteSpace(value.ModeSlug) && !string.IsNullOrWhiteSpace(value.ModeVariant)
            && !string.IsNullOrWhiteSpace(value.ReleaseVersion) && value.ArchiveSizeBytes > 0
            && IsSha256(value.ArchiveSha256) && Guid.TryParseExact(value.PreHubSnapshotCycleId, "N", out _)
            && value.InstalledAt != default
            && value.Targets != null
            && value.Targets.Count == WwmGameDefinition.Default.ManagedRelativePaths.Length
            && ValidateTargets(value.Targets)
            && (value.SchemaVersion == 1
                ? value.Targets.All(item => GetLayoutForPath(item.RelativePath) == WwmInstallationLayout.Package)
                : IsValidBinding(value.InstallationRoot, value.TargetLayout, value.InstallationFingerprint,
                    value.Targets.Select(item => item.RelativePath).ToArray()));

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(StateFile)) File.Delete(StateFile);
        await Task.CompletedTask;
    }

    public async Task WriteJournalAsync(WwmTransactionJournal journal, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(TransactionDirectory);
        await WriteAtomicAsync(JournalFile, JsonSerializer.Serialize(journal, Options), cancellationToken).ConfigureAwait(false);
    }

    public WwmTransactionJournal? LoadJournal(out string? error)
    {
        error = null;
        if (!File.Exists(JournalFile)) return null;
        try
        {
            var journal = JsonSerializer.Deserialize<WwmTransactionJournal>(File.ReadAllText(JournalFile), Options);
            var validationError = GetJournalValidationError(journal);
            if (validationError != null)
            {
                error = validationError;
                return null;
            }
            var validatedJournal = journal!;
            if (validatedJournal.PreviousStateExisted)
            {
                if (string.IsNullOrWhiteSpace(validatedJournal.PreviousStateBase64))
                    throw new InvalidDataException("Previous WWM state payload is empty.");
                var previousBytes = Convert.FromBase64String(validatedJournal.PreviousStateBase64);
                var previousJson = new System.Text.UTF8Encoding(false, true).GetString(previousBytes);
                if (!TryDeserializeState(previousJson, out _, out _))
                    throw new InvalidDataException("Previous WWM state payload failed validation.");
            }
            else if (validatedJournal.PreviousStateBase64.Length != 0)
            {
                throw new InvalidDataException("Journal contains a state payload marked absent.");
            }
            return validatedJournal;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidDataException or System.Text.DecoderFallbackException)
        {
            error = ex.Message;
            return null;
        }
    }

    public async Task RetireJournalAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(JournalFile)) File.Delete(JournalFile);
        await Task.CompletedTask;
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content, token).ConfigureAwait(false);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    internal static bool ValidateTargets(IReadOnlyList<WwmTargetManifestEntry>? targets)
    {
        if (targets == null || targets.Count != 2 || targets.Any(item => item == null
            || string.IsNullOrWhiteSpace(item.RelativePath) || item.SizeBytes <= 0 || !IsSha256(item.Sha256))) return false;
        var allowed = AllManagedRelativePaths()
            .Select(NormalizeRelative).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return targets.All(item => allowed.Contains(NormalizeRelative(item.RelativePath))
            && item.SizeBytes > 0 && IsSha256(item.Sha256))
            && targets.Select(item => NormalizeRelative(item.RelativePath).ToUpperInvariant())
                .Distinct(StringComparer.Ordinal).Count() == 2;
    }

    private static string NormalizeRelative(string path) => path.Replace('/', '\\');
    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool IsSafeAttributes(int? value)
        => value == null || (((FileAttributes)value.Value) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    internal static bool IsAllowedPath(string path)
        => !string.IsNullOrWhiteSpace(path) && AllManagedRelativePaths()
            .Contains(NormalizeRelative(path), StringComparer.Ordinal);

    internal static WwmInstallationLayout GetLayoutForPath(string path)
    {
        var normalized = NormalizeRelative(path);
        if (WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package).Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return WwmInstallationLayout.Package;
        if (WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.LocalDataPatch).Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return WwmInstallationLayout.LocalDataPatch;
        return WwmInstallationLayout.Unknown;
    }

    internal static bool IsValidBinding(string? root, string? layoutName, string? fingerprint, IReadOnlyList<string> paths)
    {
        var layout = layoutName switch
        {
            nameof(WwmInstallationLayout.Package) => WwmInstallationLayout.Package,
            nameof(WwmInstallationLayout.LocalDataPatch) => WwmInstallationLayout.LocalDataPatch,
            _ => WwmInstallationLayout.Unknown
        };
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || paths == null
            || layout is not (WwmInstallationLayout.Package or WwmInstallationLayout.LocalDataPatch)
            || fingerprint is not { Length: 64 } || !fingerprint.All(Uri.IsHexDigit)) return false;
        var expected = WwmGameDefinition.Default.GetManagedRelativePaths(layout)
            .Select(NormalizeRelative).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return paths.Count == 2 && paths.All(path => !string.IsNullOrWhiteSpace(path) && expected.Contains(NormalizeRelative(path)))
            && paths.Select(NormalizeRelative).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2;
    }

    private static string[] AllManagedRelativePaths() =>
        WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package)
            .Concat(WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.LocalDataPatch)).ToArray();

    private static string? GetJournalValidationError(WwmTransactionJournal? journal)
    {
        if (journal == null) return "WWM transaction journal is empty.";
        if (journal.SchemaVersion is not (1 or 2)) return "WWM transaction journal schema is unsupported.";
        if (!Guid.TryParseExact(journal.OperationId, "N", out _)) return "WWM transaction journal operation id is invalid.";
        if (journal.Operation is not ("install" or "restore")) return "WWM transaction journal operation is invalid.";
        if (journal.PreviousTargets == null || journal.TargetManifest == null || journal.ExpectedAbsentTargets == null)
            return "WWM transaction journal target collections are missing.";
        if (journal.PreviousTargets.Count != 2) return "WWM transaction journal previous target count is invalid.";
        if (!journal.PreviousTargets.All(item => item != null && IsAllowedPath(item.RelativePath)
            && (item.Existed ? item.SizeBytes > 0 && IsSha256(item.Sha256)
                && IsSafeAttributes(item.Attributes)
                && item.BackupFile == (Array.IndexOf(WwmGameDefinition.Default.GetManagedRelativePaths(GetLayoutForPath(item.RelativePath)), item.RelativePath) + ".bin")
            : item.SizeBytes == null && item.Sha256 == null && item.BackupFile == null && item.Attributes == null)))
            return "WWM transaction journal previous target manifest is invalid.";
        if (journal.PreviousTargets.Select(item => item.RelativePath).Distinct(StringComparer.Ordinal).Count() != 2)
            return "WWM transaction journal previous targets are duplicated.";
        if (journal.PreviousStateBase64 is null) return "WWM transaction journal previous state field is missing.";
        if (!ValidateJournalOutcome(journal)) return "WWM transaction journal expected outcome is invalid.";
        var allPaths = journal.PreviousTargets.Select(item => item.RelativePath)
            .Concat(journal.TargetManifest.Select(item => item.RelativePath)).Concat(journal.ExpectedAbsentTargets)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (journal.SchemaVersion == 1)
            return allPaths.All(path => GetLayoutForPath(path) == WwmInstallationLayout.Package)
                ? null : "Legacy WWM journal is not bound to the Package layout.";
        return IsValidBinding(journal.InstallationRoot, journal.TargetLayout, journal.InstallationFingerprint, allPaths)
            ? null : "WWM transaction journal installation binding is invalid.";
    }

    private static bool ValidateJournalOutcome(WwmTransactionJournal journal)
    {
        if (journal.TargetManifest.Any(item => item == null) || journal.ExpectedAbsentTargets.Any(path => path == null)) return false;
        var paths = journal.TargetManifest.Select(item => item.RelativePath)
            .Concat(journal.ExpectedAbsentTargets).ToList();
        return paths.Count == 2 && paths.All(IsAllowedPath)
            && paths.Select(NormalizeRelative).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2
            && paths.Select(GetLayoutForPath).Distinct().Count() == 1
            && journal.TargetManifest.All(item => item.SizeBytes > 0 && IsSha256(item.Sha256))
            && (journal.ExpectedStateSha256 == "absent" || IsSha256(journal.ExpectedStateSha256));
    }
}

public sealed class WwmTransactionJournal
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("operation_id")] public string OperationId { get; set; } = "";
    [JsonPropertyName("operation")] public string Operation { get; set; } = "install";
    [JsonPropertyName("previous_state_base64")] public string PreviousStateBase64 { get; set; } = "";
    [JsonPropertyName("previous_state_existed")] public bool PreviousStateExisted { get; set; }
    [JsonPropertyName("created_pre_hub_snapshot")] public bool CreatedPreHubSnapshot { get; set; }
    [JsonPropertyName("previous_targets")] public List<WwmPreviousTarget> PreviousTargets { get; set; } = new();
    [JsonPropertyName("target_manifest")] public List<WwmTargetManifestEntry> TargetManifest { get; set; } = new();
    [JsonPropertyName("expected_absent_targets")] public List<string> ExpectedAbsentTargets { get; set; } = new();
    [JsonPropertyName("expected_state_sha256")] public string? ExpectedStateSha256 { get; set; }
    [JsonPropertyName("game_build_id")] public string? GameBuildId { get; set; }
    [JsonPropertyName("installation_root")] public string? InstallationRoot { get; set; }
    [JsonPropertyName("target_layout")] public string? TargetLayout { get; set; }
    [JsonPropertyName("installation_fingerprint")] public string? InstallationFingerprint { get; set; }
}

public sealed class WwmPreviousTarget
{
    [JsonPropertyName("relative_path")] public string RelativePath { get; set; } = "";
    [JsonPropertyName("existed")] public bool Existed { get; set; }
    [JsonPropertyName("size_bytes")] public long? SizeBytes { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("backup_file")] public string? BackupFile { get; set; }
    [JsonPropertyName("attributes")] public int? Attributes { get; set; }
}

public sealed class WwmPreHubSnapshot
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("cycle_operation_id")] public string CycleOperationId { get; set; } = "";
    [JsonPropertyName("game_build_id")] public string? GameBuildId { get; set; }
    [JsonPropertyName("installation_root")] public string? InstallationRoot { get; set; }
    [JsonPropertyName("target_layout")] public string? TargetLayout { get; set; }
    [JsonPropertyName("installation_fingerprint")] public string? InstallationFingerprint { get; set; }
    [JsonPropertyName("targets")] public List<WwmPreviousTarget> Targets { get; set; } = new();
}
