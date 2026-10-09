using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BdoClient;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient.Update;

public sealed class UpdatePackageService
{
    private const string ExeFileName = ApplicationTechnicalIdentity.ExecutableFileName;
    private const string ManifestFileName = "release-manifest.json";
    public const long ZipMaxBytes = 100_000_000;

    private readonly GitHubUpdateClient _gitHubClient;
    private readonly UpdateManifestValidator _manifestValidator;
    private readonly UpdateSessionStore _sessionStore;
    private readonly AppPaths _appPaths;
    private readonly ILogger _logger;

    public UpdatePackageService(
        GitHubUpdateClient gitHubClient,
        UpdateManifestValidator manifestValidator,
        UpdateSessionStore sessionStore,
        AppPaths appPaths,
        ILogger logger)
    {
        _gitHubClient = gitHubClient;
        _manifestValidator = manifestValidator;
        _sessionStore = sessionStore;
        _appPaths = appPaths;
        _logger = logger;
    }

    public async Task<UpdatePackageResult> StageUpdateAsync(
        UpdateCandidate candidate,
        AppVersionInfo currentVersionInfo,
        IProgress<UpdateStageProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!currentVersionInfo.IsPublicRelease || !currentVersionInfo.PublicVersion.HasValue)
        {
            _logger.Warning("Update staging rejected: current version is not a public release");
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Current version is not a public release");
        }

        if (candidate.Version <= currentVersionInfo.PublicVersion.Value)
        {
            _logger.Warning($"Update staging rejected: candidate {candidate.Version} <= current {currentVersionInfo.PublicVersion.Value}");
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Candidate version is not newer than current");
        }

        if (candidate.Release.Draft)
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Candidate release is a draft");

        if (!candidate.Release.PublishedAt.HasValue)
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Candidate release is not published");

        var expectedTag = $"v{candidate.Version}";
        if (!string.Equals(candidate.TagName, expectedTag, StringComparison.Ordinal))
            return UpdatePackageResult.Failure(UpdatePackageError.InvalidCandidate, "Candidate tag does not match version");

        var sessionId = Guid.NewGuid().ToString("D");
        var sessionDir = _sessionStore.GetSessionDir(sessionId);
        _logger.Info($"Update staging started: {candidate.TagName} (session={sessionId})");

        var keepSession = false;
        string? packageFileName = null;
        try
        {
            Directory.CreateDirectory(sessionDir);

            progress?.Report(new UpdateStageProgress("Отримання метаданих оновлення...", 0));
            var exePath = Path.Combine(sessionDir, ExeFileName);
            var downloadProgress = new Progress<double>(pct =>
                progress?.Report(new UpdateStageProgress($"Завантаження оновлення {candidate.TagName}...", pct)));

            string packageAssetName = ExeFileName;
            string packageSha;
            string expectedSha;
            var manifestAssetCount = candidate.Release.Assets?.Count(a =>
                string.Equals(a.Name, ManifestFileName, StringComparison.Ordinal)) ?? 0;
            if (manifestAssetCount == 0)
            {
                var bundleName = ResolveBundleAssetName(candidate);
                if (bundleName == null)
                    return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "No unambiguous supported bundle asset found");
                var packageAsset = FindExactlyOneAsset(candidate, bundleName);
                if (packageAsset == null)
                    return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "Selected bundle not found or ambiguous");
                if (packageAsset.Size <= 0 || packageAsset.Size > ZipMaxBytes)
                    return UpdatePackageResult.Failure(UpdatePackageError.SizeMismatch, $"Invalid ZIP size: {packageAsset.Size}");
                if (!ValidateGitHubDigest(packageAsset, expectedSha: null, requireDigest: true))
                    return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "ZIP GitHub digest missing, malformed, or unsupported");

                var packagePath = Path.Combine(sessionDir, bundleName);
                packageFileName = bundleName;
                var packageResult = await _gitHubClient.DownloadAssetAsync(
                    packageAsset.BrowserDownloadUrl!, packagePath, packageAsset.Size, downloadProgress, cancellationToken);
                if (!packageResult.IsSuccess)
                    return UpdatePackageResult.Failure(UpdatePackageError.DownloadFailed, packageResult.ErrorMessage!);
                packageSha = packageResult.Value!.Sha256;
                if (!ValidateGitHubDigest(packageAsset, packageSha, requireDigest: true))
                    return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "ZIP SHA-256 mismatch");
                var extractionResult = await ExtractValidatedBundleAsync(packagePath, exePath, candidate, cancellationToken, _manifestValidator);
                if (!extractionResult.IsValid)
                    return UpdatePackageResult.Failure(UpdatePackageError.PackageInvalid, extractionResult.Error!);
                expectedSha = extractionResult.ExeSha256!;
            }
            else
            {
                var manifestAsset = FindExactlyOneAsset(candidate, ManifestFileName);
                if (manifestAsset == null)
                    return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "Manifest asset not found or ambiguous");
                var manifestResult = await _gitHubClient.FetchManifestAsync(manifestAsset, cancellationToken);
                if (!manifestResult.IsSuccess)
                    return UpdatePackageResult.Failure(UpdatePackageError.ManifestDownloadFailed, manifestResult.ErrorMessage!);
                var manifest = manifestResult.Value!;
                var validationResult = _manifestValidator.Validate(manifest, candidate);
                if (!validationResult.IsValid)
                    return UpdatePackageResult.Failure(UpdatePackageError.ManifestInvalid, validationResult.ErrorMessage!);
                expectedSha = validationResult.NormalizedSha256!;

                if (!string.IsNullOrWhiteSpace(manifest.PackageName) && !string.IsNullOrWhiteSpace(manifest.PackageSha256))
                {
                    var packageAsset = FindExactlyOneAsset(candidate, manifest.PackageName);
                    if (packageAsset == null)
                        return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "ZIP package not found or ambiguous");
                    if (packageAsset.Size <= 0 || packageAsset.Size > ZipMaxBytes)
                        return UpdatePackageResult.Failure(UpdatePackageError.SizeMismatch, $"Invalid ZIP size: {packageAsset.Size}");
                    if (!ValidateGitHubDigest(packageAsset, manifest.PackageSha256, requireDigest: false))
                        return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "ZIP GitHub digest mismatch or unsupported format");

                    var packagePath = Path.Combine(sessionDir, manifest.PackageName);
                    packageFileName = manifest.PackageName;
                    var packageResult = await _gitHubClient.DownloadAssetAsync(
                        packageAsset.BrowserDownloadUrl!, packagePath, packageAsset.Size, downloadProgress, cancellationToken);
                    if (!packageResult.IsSuccess)
                        return UpdatePackageResult.Failure(UpdatePackageError.DownloadFailed, packageResult.ErrorMessage!);
                    packageSha = packageResult.Value!.Sha256;
                    if (!string.Equals(packageSha, manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
                        return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "ZIP SHA-256 mismatch");
                    var extractionResult = await ExtractValidatedExeAsync(
                        packagePath, exePath, expectedSha, candidate.Version, cancellationToken,
                        expectedEntryName: manifest.AssetName);
                    if (!extractionResult.IsValid)
                        return UpdatePackageResult.Failure(UpdatePackageError.PackageInvalid, extractionResult.Error!);
                }
                else
                {
                    var exeAsset = FindExactlyOneAsset(candidate, manifest.AssetName!);
                    if (exeAsset == null)
                        return UpdatePackageResult.Failure(UpdatePackageError.AssetMissing, "EXE asset not found or ambiguous");
                    if (exeAsset.Size <= 0 || exeAsset.Size > GitHubUpdateClient.ExeMaxBytes)
                        return UpdatePackageResult.Failure(UpdatePackageError.SizeMismatch, $"Invalid EXE size: {exeAsset.Size}");
                    if (!ValidateGitHubDigest(exeAsset, expectedSha, requireDigest: false))
                        return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "GitHub digest mismatch or unsupported format");
                    var downloadResult = await _gitHubClient.DownloadAssetAsync(
                        exeAsset.BrowserDownloadUrl!, exePath, exeAsset.Size, downloadProgress, cancellationToken);
                    if (!downloadResult.IsSuccess)
                        return UpdatePackageResult.Failure(UpdatePackageError.DownloadFailed, downloadResult.ErrorMessage!);
                    packageSha = downloadResult.Value!.Sha256;
                }
            }

            progress?.Report(new UpdateStageProgress("Перевірка цілісності...", 100));
            var downloadedExeSha = await HashHelper.ComputeFileSha256Async(exePath, cancellationToken);
            if (!string.Equals(downloadedExeSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error($"EXE SHA mismatch: actual={downloadedExeSha} expected={expectedSha}");
                return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "EXE SHA-256 mismatch");
            }

            progress?.Report(new UpdateStageProgress("Перевірка оновлення...", 100));
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(exePath);
            if (!ExecutableVersionValidator.Validate(fileVersionInfo.FileVersion, fileVersionInfo.ProductVersion, candidate.Version, out var versionError))
                return UpdatePackageResult.Failure(UpdatePackageError.ExecutableInvalid, versionError!);

            var exeSha = await HashHelper.ComputeFileSha256Async(exePath, cancellationToken);
            if (!string.Equals(exeSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                return UpdatePackageResult.Failure(UpdatePackageError.HashMismatch, "Staged EXE SHA-256 mismatch");

            var targetPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(targetPath) || !Path.IsPathRooted(targetPath) || !File.Exists(targetPath))
                return UpdatePackageResult.Failure(UpdatePackageError.IoError, "Current executable file is not available");

            progress?.Report(new UpdateStageProgress("Підготовка оновлення...", 100));
            var session = new UpdateSession
            {
                SchemaVersion = 1,
                SessionId = sessionId,
                CreatedAt = DateTimeOffset.UtcNow,
                State = UpdateSession.StateStaged,
                CurrentVersion = currentVersionInfo.PublicVersion?.ToString() ?? currentVersionInfo.RawVersion,
                TargetVersion = candidate.Version.ToString(),
                TargetTag = candidate.TagName,
                TargetPath = targetPath,
                ParentPid = Environment.ProcessId,
                PackageAssetName = packageAssetName,
                PackageFileName = packageFileName,
                PackageSha256 = packageSha,
                StagedExeSha256 = exeSha
            };

            var writeResult = _sessionStore.WriteSession(session);
            if (!writeResult.IsSuccess)
                return writeResult;

            keepSession = true;
            _logger.Info($"Update staging complete: {candidate.TagName} (session={sessionId})");
            return UpdatePackageResult.Success(session);
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"Update staging cancelled (session={sessionId})");
            return UpdatePackageResult.Failure(UpdatePackageError.Cancelled, "Cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error($"Update staging failed: {ex.Message}");
            return UpdatePackageResult.Failure(UpdatePackageError.IoError, ex.Message);
        }
        finally
        {
            if (!keepSession)
                _sessionStore.CleanupSession(sessionId,
                    packageFileName == null
                        ? new[] { ExeFileName }
                        : new[] { ExeFileName, packageFileName });
        }
    }

    internal static async Task<(bool IsValid, string? Error)> ExtractValidatedExeAsync(
        string packagePath,
        string exePath,
        string expectedSha,
        AppVersion version,
        CancellationToken cancellationToken = default,
        Func<string?, string?, bool>? versionValidator = null,
        string? expectedEntryName = null)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            if (archive.Entries.Count != 1)
                return (false, "ZIP must contain exactly one entry");
            var entry = archive.Entries[0];
            expectedEntryName ??= ApplicationTechnicalIdentity.ExecutableFileName;
            if (!string.Equals(entry.FullName, expectedEntryName, StringComparison.Ordinal) || entry.FullName.Contains('/') || entry.FullName.Contains('\\'))
                return (false, $"ZIP entry must be exactly {expectedEntryName} at archive root");
            if (entry.Length <= 0 || entry.Length > GitHubUpdateClient.ExeMaxBytes)
                return (false, "ZIP EXE entry size is invalid");
            using var input = entry.Open();
            await using (var output = new FileStream(exePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            var stagedSha = await HashHelper.ComputeFileSha256Async(exePath, cancellationToken);
            if (!string.Equals(stagedSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                return (false, "Extracted EXE SHA-256 mismatch");
            var info = FileVersionInfo.GetVersionInfo(exePath);
            if (versionValidator == null)
            {
                if (!ExecutableVersionValidator.Validate(info.FileVersion, info.ProductVersion, version, out var error))
                    return (false, error);
            }
            else if (!versionValidator(info.FileVersion, info.ProductVersion))
            {
                return (false, "Executable version metadata mismatch");
            }
            return (true, null);
        }
        catch (InvalidDataException ex)
        {
            return (false, $"Malformed ZIP: {ex.Message}");
        }
        catch (IOException ex)
        {
            return (false, $"ZIP extraction failed: {ex.Message}");
        }
    }

    internal static async Task<(bool IsValid, string? Error, string? ExeSha256)> ExtractValidatedBundleAsync(
        string packagePath,
        string exePath,
        UpdateCandidate candidate,
        CancellationToken cancellationToken = default,
        UpdateManifestValidator? manifestValidator = null,
        Func<string?, string?, bool>? versionValidator = null,
        string? expectedEntryName = null)
    {
        try
        {
            if (expectedEntryName == null)
            {
                var version = candidate.Version.ToString();
                if (candidate.BundleAssetName == null || candidate.BundleAssetName == ApplicationTechnicalIdentity.BuildPackageFileName(version))
                    expectedEntryName = ApplicationTechnicalIdentity.ExecutableFileName;
                else if (candidate.BundleAssetName == ApplicationTechnicalIdentity.BuildLegacyPackageFileName(version))
                    expectedEntryName = ApplicationTechnicalIdentity.LegacyExecutableFileName;
                else
                    return (false, "Unsupported bundle package identity", null);
            }
            if (!ApplicationTechnicalIdentity.IsSupportedExecutableFileName(expectedEntryName))
                return (false, "Unsupported bundle executable identity", null);
            using var archive = ZipFile.OpenRead(packagePath);
            var expectedNames = new HashSet<string>(StringComparer.Ordinal)
            {
                expectedEntryName,
                ManifestFileName,
                "SHA256SUMS.txt",
                $"RELEASE_NOTES-v{candidate.Version}.md"
            };
            if (archive.Entries.Count != expectedNames.Count ||
                archive.Entries.Any(entry => !expectedNames.Contains(entry.FullName)) ||
                archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != expectedNames.Count)
                return (false, "Bundle must contain exactly the four expected root-level entries", null);

            var manifestEntry = archive.GetEntry(ManifestFileName)!;
            var sumsEntry = archive.GetEntry("SHA256SUMS.txt")!;
            var manifestJson = await ReadZipEntryTextAsync(manifestEntry, 65536, cancellationToken);
            var sumsText = await ReadZipEntryTextAsync(sumsEntry, 65536, cancellationToken);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (manifest == null)
                return (false, "Bundle manifest is empty", null);
            manifestValidator ??= new UpdateManifestValidator(new SilentLogger());
            var validation = manifestValidator.ValidateBundle(manifest, candidate, expectedEntryName);
            if (!validation.IsValid)
                return (false, validation.ErrorMessage, null);

            var sumsLine = sumsText.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (sumsLine.EndsWith('\n'))
                sumsLine = sumsLine[..^1];
            if (sumsLine.Contains('\n'))
                return (false, "SHA256SUMS.txt must contain exactly one line", null);
            var expectedSumsLine = $"{validation.NormalizedSha256}  {expectedEntryName}";
            if (!string.Equals(sumsLine, expectedSumsLine, StringComparison.OrdinalIgnoreCase))
                return (false, "SHA256SUMS.txt does not match the bundle manifest", null);

            var entry = archive.GetEntry(expectedEntryName)!;
            if (entry.Length <= 0 || entry.Length > GitHubUpdateClient.ExeMaxBytes)
                return (false, "Bundle EXE entry size is invalid", null);
            using var input = entry.Open();
            await using (var output = new FileStream(exePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            var stagedSha = await HashHelper.ComputeFileSha256Async(exePath, cancellationToken);
            if (!string.Equals(stagedSha, validation.NormalizedSha256, StringComparison.OrdinalIgnoreCase))
                return (false, "Extracted EXE SHA-256 mismatch", null);
            var info = FileVersionInfo.GetVersionInfo(exePath);
            string? versionError = null;
            var versionValid = versionValidator == null
                ? ExecutableVersionValidator.Validate(info.FileVersion, info.ProductVersion, candidate.Version, out versionError)
                : versionValidator(info.FileVersion, info.ProductVersion);
            if (!versionValid)
                return (false, versionValidator == null ? versionError : "Executable version metadata mismatch", null);
            return (true, null, validation.NormalizedSha256);
        }
        catch (InvalidDataException ex)
        {
            return (false, $"Malformed ZIP: {ex.Message}", null);
        }
        catch (IOException ex)
        {
            return (false, $"ZIP extraction failed: {ex.Message}", null);
        }
        catch (JsonException ex)
        {
            return (false, $"Malformed bundle manifest: {ex.Message}", null);
        }
        catch (DecoderFallbackException ex)
        {
            return (false, $"Invalid bundle text encoding: {ex.Message}", null);
        }
    }

    private static async Task<string> ReadZipEntryTextAsync(ZipArchiveEntry entry, int maxBytes, CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > maxBytes)
            throw new InvalidDataException("ZIP metadata entry exceeds maximum size");
        return new UTF8Encoding(false, true).GetString(buffer.ToArray());
    }

    private bool ValidateGitHubDigest(GitHubReleaseAsset asset, string? expectedSha, bool requireDigest)
    {
        if (string.IsNullOrEmpty(asset.Digest))
            return !requireDigest;

        if (!asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning($"Unsupported GitHub digest format: {asset.Digest}");
            return false;
        }

        var digest = asset.Digest["sha256:".Length..];
        if (digest.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(digest, "^[0-9a-fA-F]{64}$"))
        {
            _logger.Error($"Malformed GitHub digest: {asset.Digest}");
            return false;
        }

        return expectedSha == null || string.Equals(digest, expectedSha, StringComparison.OrdinalIgnoreCase);
    }

    internal static GitHubReleaseAsset? FindExactlyOneAsset(UpdateCandidate candidate, string assetName)
    {
        var matches = candidate.Release.Assets?
            .Where(a => string.Equals(a.Name, assetName, StringComparison.Ordinal))
            .ToList();

        if (matches == null || matches.Count != 1)
            return null;

        var asset = matches[0];
        if (!string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase) ||
            asset.Size <= 0 || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
            return null;

        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            return null;

        return asset;
    }

    private static string? ResolveBundleAssetName(UpdateCandidate candidate)
    {
        var version = candidate.Version.ToString();
        var canonical = ApplicationTechnicalIdentity.BuildPackageFileName(version);
        var legacy = ApplicationTechnicalIdentity.BuildLegacyPackageFileName(version);
        if (candidate.BundleAssetName != null)
            return candidate.BundleAssetName == canonical || candidate.BundleAssetName == legacy
                ? candidate.BundleAssetName
                : null;

        var canonicalCount = candidate.Release.Assets?.Count(a => a.Name == canonical) ?? 0;
        var legacyCount = candidate.Release.Assets?.Count(a => a.Name == legacy) ?? 0;
        if (canonicalCount > 1 || legacyCount > 1 || canonicalCount == 0 && legacyCount == 0)
            return null;
        return canonicalCount == 1 ? canonical : legacy;
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private sealed class SilentLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

public sealed class UpdateStageProgress
{
    public string Message { get; }
    public double Percent { get; }

    public UpdateStageProgress(string message, double percent)
    {
        Message = message;
        Percent = percent;
    }
}
