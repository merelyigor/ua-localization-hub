using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BdoClient;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient.Update;

public enum UpdateSessionLoadStatus
{
    Missing,
    Valid,
    Invalid
}

public sealed class UpdateSessionLoadResult
{
    public UpdateSessionLoadStatus Status { get; }
    public UpdateSession? Session { get; }

    private UpdateSessionLoadResult(UpdateSessionLoadStatus status, UpdateSession? session)
    {
        Status = status;
        Session = session;
    }

    public static UpdateSessionLoadResult Missing => new(UpdateSessionLoadStatus.Missing, null);
    public static UpdateSessionLoadResult Valid(UpdateSession session) => new(UpdateSessionLoadStatus.Valid, session);
    public static UpdateSessionLoadResult Invalid => new(UpdateSessionLoadStatus.Invalid, null);
}

public sealed class UpdateSessionStore
{
    private const string SessionFileName = "update-session.json";
    private const string GuidDRegexPattern = @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
    private const string Sha256HexPattern = @"^[0-9a-fA-F]{64}$";

    private static readonly Regex GuidDRegex = new(GuidDRegexPattern, RegexOptions.Compiled);
    private static readonly Regex Sha256HexRegex = new(Sha256HexPattern, RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly HashSet<string> SupportedStates = new(StringComparer.Ordinal)
    {
        UpdateSession.StateStaged,
        UpdateSession.StatePrepared,
        UpdateSession.StateApplied
    };

    private readonly AppPaths _appPaths;
    private readonly ILogger _logger;

    internal Func<string, bool>? DeleteFileOverride { get; set; }

    public UpdateSessionStore(AppPaths appPaths, ILogger logger)
    {
        _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static string NormalizeSessionId(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) || !GuidDRegex.IsMatch(sessionId))
            throw new ArgumentException("Session ID must be in D-format (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)", nameof(sessionId));

        if (!Guid.TryParse(sessionId, out var guid))
            throw new ArgumentException("Invalid session ID", nameof(sessionId));

        return guid.ToString("D");
    }

    public static bool IsValidSessionId(string sessionId)
    {
        return !string.IsNullOrEmpty(sessionId) && GuidDRegex.IsMatch(sessionId);
    }

    public static bool IsValidSha256Hex(string? value)
    {
        return !string.IsNullOrEmpty(value) && Sha256HexRegex.IsMatch(value);
    }

    private static bool IsValidPackageFileName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || (!Path.IsPathRooted(value)
                && !value.Contains(Path.DirectorySeparatorChar)
                && !value.Contains(Path.AltDirectorySeparatorChar)
                && value != "."
                && value != ".."
                && !string.Equals(value, "update-session.json", StringComparison.OrdinalIgnoreCase));
    }

    public string GetSessionDir(string sessionId)
    {
        var normalized = NormalizeSessionId(sessionId);
        var dir = Path.Combine(_appPaths.UpdatesDir, normalized);
        var normalizedDir = Path.GetFullPath(dir);
        var normalizedRoot = Path.GetFullPath(_appPaths.UpdatesDir);

        if (!normalizedDir.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Session path escapes updates directory", nameof(sessionId));

        return normalizedDir;
    }

    public AppPaths AppPaths => _appPaths;

    public UpdatePackageResult WriteSession(UpdateSession session)
    {
        try
        {
            var normalizedId = NormalizeSessionId(session.SessionId);
            var sessionDir = GetSessionDir(normalizedId);
            Directory.CreateDirectory(sessionDir);

            var filePath = Path.Combine(sessionDir, SessionFileName);
            var tempPath = filePath + ".tmp";

            var json = JsonSerializer.Serialize(session, JsonOptions);
            File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);

            File.Move(tempPath, filePath, overwrite: true);

            _logger.Debug($"Session {normalizedId}: written to {filePath}");
            return UpdatePackageResult.Success(session);
        }
        catch (Exception ex)
        {
            _logger.Error($"Session write failed: {ex.Message}");
            return UpdatePackageResult.Failure(UpdatePackageError.SessionWriteFailed, ex.Message);
        }
    }

    public UpdateSessionLoadResult LoadSession(string sessionId)
    {
        return LoadSessionForState(sessionId, UpdateSession.StateStaged);
    }

    public UpdateSessionLoadResult LoadSessionAnyState(string sessionId)
    {
        try
        {
            var normalizedId = NormalizeSessionId(sessionId);
            var sessionDir = GetSessionDir(normalizedId);
            var filePath = Path.Combine(sessionDir, SessionFileName);

            if (!File.Exists(filePath))
                return UpdateSessionLoadResult.Missing;

            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var session = JsonSerializer.Deserialize<UpdateSession>(json, JsonOptions);

            if (session == null)
                return UpdateSessionLoadResult.Invalid;

            return ValidateSessionAnyState(session, normalizedId);
        }
        catch (JsonException ex)
        {
            _logger.Warning($"Session JSON parse failed for {sessionId}: {ex.Message}");
            return UpdateSessionLoadResult.Invalid;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Session read failed for {sessionId}: {ex.Message}");
            return UpdateSessionLoadResult.Invalid;
        }
    }

    public UpdateSessionLoadResult LoadSessionForState(string sessionId, string expectedState)
    {
        if (!SupportedStates.Contains(expectedState))
        {
            _logger.Warning($"LoadSessionForState: unsupported expected state '{expectedState}'");
            return UpdateSessionLoadResult.Invalid;
        }

        try
        {
            var normalizedId = NormalizeSessionId(sessionId);
            var sessionDir = GetSessionDir(normalizedId);
            var filePath = Path.Combine(sessionDir, SessionFileName);

            if (!File.Exists(filePath))
                return UpdateSessionLoadResult.Missing;

            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var session = JsonSerializer.Deserialize<UpdateSession>(json, JsonOptions);

            if (session == null)
                return UpdateSessionLoadResult.Invalid;

            return ValidateSession(session, normalizedId, expectedState);
        }
        catch (JsonException ex)
        {
            _logger.Warning($"Session JSON parse failed for {sessionId}: {ex.Message}");
            return UpdateSessionLoadResult.Invalid;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Session read failed for {sessionId}: {ex.Message}");
            return UpdateSessionLoadResult.Invalid;
        }
    }

    private static UpdateSessionLoadResult ValidateSession(UpdateSession session, string expectedId, string expectedState)
    {
        if (session.SchemaVersion != 1)
            return UpdateSessionLoadResult.Invalid;

        if (!IsValidSessionId(session.SessionId))
            return UpdateSessionLoadResult.Invalid;

        var normalizedStoredId = NormalizeSessionId(session.SessionId);
        if (!string.Equals(normalizedStoredId, expectedId, StringComparison.Ordinal))
            return UpdateSessionLoadResult.Invalid;

        if (!string.Equals(session.State, expectedState, StringComparison.Ordinal))
            return UpdateSessionLoadResult.Invalid;

        if (session.CreatedAt == default)
            return UpdateSessionLoadResult.Invalid;

        var currentVersion = AppVersion.TryParseCoreVersion(session.CurrentVersion);
        if (!currentVersion.HasValue)
            return UpdateSessionLoadResult.Invalid;

        var targetVersion = AppVersion.TryParseCoreVersion(session.TargetVersion);
        if (!targetVersion.HasValue)
            return UpdateSessionLoadResult.Invalid;

        if (targetVersion.Value <= currentVersion.Value)
            return UpdateSessionLoadResult.Invalid;

        var expectedTag = $"v{targetVersion.Value}";
        if (!string.Equals(session.TargetTag, expectedTag, StringComparison.Ordinal))
            return UpdateSessionLoadResult.Invalid;

        if (string.IsNullOrWhiteSpace(session.TargetPath) || !Path.IsPathRooted(session.TargetPath))
            return UpdateSessionLoadResult.Invalid;

        if (session.ParentPid <= 0)
            return UpdateSessionLoadResult.Invalid;

        if (!ApplicationTechnicalIdentity.IsSupportedExecutableFileName(session.PackageAssetName))
            return UpdateSessionLoadResult.Invalid;

        if (!IsValidSha256Hex(session.PackageSha256))
            return UpdateSessionLoadResult.Invalid;

        if (!IsValidPackageFileName(session.PackageFileName))
            return UpdateSessionLoadResult.Invalid;

        if (!IsValidSha256Hex(session.StagedExeSha256))
            return UpdateSessionLoadResult.Invalid;

        // State-specific: original_exe_sha256
        if (expectedState == UpdateSession.StatePrepared || expectedState == UpdateSession.StateApplied)
        {
            if (!IsValidSha256Hex(session.OriginalExeSha256))
                return UpdateSessionLoadResult.Invalid;
        }

        return UpdateSessionLoadResult.Valid(session);
    }

    private static UpdateSessionLoadResult ValidateSessionAnyState(UpdateSession session, string expectedId)
    {
        if (session.SchemaVersion != 1)
            return UpdateSessionLoadResult.Invalid;

        if (!IsValidSessionId(session.SessionId))
            return UpdateSessionLoadResult.Invalid;

        var normalizedStoredId = NormalizeSessionId(session.SessionId);
        if (!string.Equals(normalizedStoredId, expectedId, StringComparison.Ordinal))
            return UpdateSessionLoadResult.Invalid;

        if (!SupportedStates.Contains(session.State))
            return UpdateSessionLoadResult.Invalid;

        if (session.CreatedAt == default)
            return UpdateSessionLoadResult.Invalid;

        var currentVersion = AppVersion.TryParseCoreVersion(session.CurrentVersion);
        if (!currentVersion.HasValue)
            return UpdateSessionLoadResult.Invalid;

        var targetVersion = AppVersion.TryParseCoreVersion(session.TargetVersion);
        if (!targetVersion.HasValue)
            return UpdateSessionLoadResult.Invalid;

        if (targetVersion.Value <= currentVersion.Value)
            return UpdateSessionLoadResult.Invalid;

        var expectedTag = $"v{targetVersion.Value}";
        if (!string.Equals(session.TargetTag, expectedTag, StringComparison.Ordinal))
            return UpdateSessionLoadResult.Invalid;

        if (string.IsNullOrWhiteSpace(session.TargetPath) || !Path.IsPathRooted(session.TargetPath))
            return UpdateSessionLoadResult.Invalid;

        if (session.ParentPid <= 0)
            return UpdateSessionLoadResult.Invalid;

        if (!ApplicationTechnicalIdentity.IsSupportedExecutableFileName(session.PackageAssetName))
            return UpdateSessionLoadResult.Invalid;

        if (!IsValidSha256Hex(session.PackageSha256))
            return UpdateSessionLoadResult.Invalid;

        if (!IsValidPackageFileName(session.PackageFileName))
            return UpdateSessionLoadResult.Invalid;

        if (!IsValidSha256Hex(session.StagedExeSha256))
            return UpdateSessionLoadResult.Invalid;

        if (session.State == UpdateSession.StatePrepared || session.State == UpdateSession.StateApplied)
        {
            if (!IsValidSha256Hex(session.OriginalExeSha256))
                return UpdateSessionLoadResult.Invalid;
        }

        return UpdateSessionLoadResult.Valid(session);
    }

    public bool CleanupSession(string sessionId, IEnumerable<string>? knownOwnedFiles = null)
    {
        try
        {
            var normalizedId = NormalizeSessionId(sessionId);
            var sessionDir = GetSessionDir(normalizedId);
            if (!Directory.Exists(sessionDir))
                return true;

            var loadResult = LoadSessionAnyState(normalizedId);
            if (loadResult.Status != UpdateSessionLoadStatus.Valid || loadResult.Session == null)
            {
                if (knownOwnedFiles != null)
                {
                    var preMetadataOwnedNames = new HashSet<string>(knownOwnedFiles, StringComparer.OrdinalIgnoreCase)
                    {
                        SessionFileName
                    };
                    var preMetadataEntries = Directory.GetFileSystemEntries(sessionDir);
                    if (preMetadataEntries.Any(entry => Directory.Exists(entry)
                        || !preMetadataOwnedNames.Contains(Path.GetFileName(entry))))
                    {
                        _logger.Warning($"Session {normalizedId}: cleanup retained because unknown files are present");
                        return false;
                    }

                    foreach (var entry in preMetadataEntries)
                    {
                        if (!TryDeleteFileWithRetry(entry))
                            return false;
                    }

                    Directory.Delete(sessionDir, recursive: false);
                    return true;
                }

                _logger.Warning($"Session {normalizedId}: cleanup retained because metadata is not valid");
                return false;
            }

            var session = loadResult.Session;
            var legacyPackageName = GetLegacyPackageFileName(session);
            var packageName = string.IsNullOrWhiteSpace(session.PackageFileName)
                ? legacyPackageName
                : session.PackageFileName;
            var ownedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SessionFileName,
                session.PackageAssetName
            };

            if (!string.IsNullOrWhiteSpace(packageName))
                ownedNames.Add(packageName);

            var entries = Directory.GetFileSystemEntries(sessionDir);
            if (entries.Any(entry => Directory.Exists(entry)
                || !ownedNames.Contains(Path.GetFileName(entry)) &&
                   !string.Equals(Path.GetFileName(entry), SessionFileName + ".tmp", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.Warning($"Session {normalizedId}: cleanup retained because unknown files are present");
                return false;
            }

            if (!VerifyOwnedFileHash(
                Path.Combine(sessionDir, session.PackageAssetName),
                session.StagedExeSha256,
                "helper EXE"))
                return false;

            if (!VerifyOwnedFileHash(
                packageName == null ? null : Path.Combine(sessionDir, packageName),
                session.PackageSha256,
                "package ZIP"))
                return false;

            var workspace = ReplacementWorkspace.Derive(_appPaths, normalizedId, session.TargetPath);
            if (!workspace.TryDeleteOwnedFallbackWorkspace())
            {
                _logger.Warning($"Session {normalizedId}: cleanup retained because fallback workspace is not empty");
                return false;
            }

            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (string.Equals(name, SessionFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryDeleteFileWithRetry(entry))
                {
                    _logger.Warning($"Session {normalizedId}: cleanup retained because '{name}' could not be deleted");
                    return false;
                }
            }

            var metadataPath = Path.Combine(sessionDir, SessionFileName);
            if (!TryDeleteFileWithRetry(metadataPath))
            {
                _logger.Warning($"Session {normalizedId}: cleanup retained because session metadata could not be deleted");
                return false;
            }

            Directory.Delete(sessionDir, recursive: false);
            _logger.Debug($"Session {normalizedId}: cleaned up");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Session cleanup failed for {sessionId}: {ex.Message}");
            return false;
        }
    }

    private bool TryDeleteFileWithRetry(string path)
    {
        const int attempts = 3;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var deleted = DeleteFileOverride?.Invoke(path) ?? DeleteFile(path);
                if (deleted && !File.Exists(path))
                    return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == attempts)
                    _logger.Warning($"Session file cleanup failed for {path}: {ex.Message}");
            }

            if (attempt < attempts)
                Thread.Sleep(50 * attempt);
        }

        return !File.Exists(path);
    }

    private static bool DeleteFile(string path)
    {
        if (!File.Exists(path))
            return true;

        File.Delete(path);
        return !File.Exists(path);
    }

    private static string? GetLegacyPackageFileName(UpdateSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.PackageFileName))
            return null;

        var targetVersion = AppVersion.TryParseCoreVersion(session.TargetVersion);
        return targetVersion.HasValue
            ? ApplicationTechnicalIdentity.BuildPackageFileName(session.PackageAssetName, targetVersion.Value.ToString())
            : null;
    }

    private bool VerifyOwnedFileHash(string? path, string expectedSha, string fileType)
    {
        if (path == null || !File.Exists(path))
            return true;

        try
        {
            var actualSha = HashHelper.ComputeFileSha256(path);
            if (string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                return true;

            _logger.Warning($"Session cleanup retained because {fileType} SHA mismatch at {path}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Session cleanup retained because {fileType} hash verification failed: {ex.Message}");
            return false;
        }
    }
}
