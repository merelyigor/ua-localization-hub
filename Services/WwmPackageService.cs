using System.IO.Compression;
using System.Security.Cryptography;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Services;

public static class WwmPackageResolver
{
    public static IReadOnlyList<WwmMode> GetInstallableModes(WwmReleaseFeed? feed)
    {
        var result = new List<WwmMode>();
        foreach (var mode in feed?.Data?.Modes?.AsEnumerable() ?? Enumerable.Empty<WwmMode>())
        {
            if (!string.IsNullOrWhiteSpace(mode.Slug) && !string.IsNullOrWhiteSpace(mode.Variant)
                && TryResolve(feed, mode.Slug, mode.Variant, out _, out _))
                result.Add(mode);
        }
        return result;
    }

    public static bool TryResolve(WwmReleaseFeed? feed, string slug, string variant, out WwmApiPackage? package, out string? error)
    {
        package = null;
        error = null;
        if (feed?.Success != true || feed.Data?.Current == null || feed.Data.Modes == null)
        {
            error = "No current WWM release is available.";
            return false;
        }

        var modes = feed.Data.Modes.Where(item => item.Slug == slug && item.Variant == variant).Take(2).ToArray();
        var mode = modes.Length == 1 ? modes[0] : null;
        if (mode == null || !mode.Available)
        {
            error = "The selected WWM mode is unavailable.";
            return false;
        }

        var files = feed.Data.Current.Files?.Where(item => item.Slug == slug && item.Variant == variant).Take(2).ToArray();
        var file = files is { Length: 1 } ? files[0] : null;
        if (file == null || !Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || file.SizeBytes is null or <= 0 or > 1_000_000_000
            || !IsSha256(file.Sha256)
            || mode.DownloadUrl != file.DownloadUrl || mode.SizeBytes != file.SizeBytes
            || !string.Equals(mode.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(feed.Data.Current.Version))
        {
            error = "The selected WWM mode has invalid or inconsistent current package metadata.";
            return false;
        }

        var sizeBytes = file.SizeBytes.GetValueOrDefault();
        var sha256 = file.Sha256!;
        package = new WwmApiPackage
        {
            Mode = mode,
            Version = feed.Data.Current.Version,
            DownloadUrl = uri.AbsoluteUri,
            SizeBytes = sizeBytes,
            Sha256 = sha256.ToLowerInvariant()
        };
        return true;
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed class WwmPackageService
{
    private const long MaximumArchiveBytes = 1_000_000_000;
    private const long MaximumExtractedBytes = 700_000_000;
    private const int MaximumEntries = 32;
    private readonly HttpClient _httpClient;
    private readonly GamePersistencePaths _paths;
    private readonly ILogger _logger;

    public WwmPackageService(HttpClient httpClient, GamePersistencePaths paths, ILogger logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WwmStagedPackage> DownloadAndStageAsync(WwmApiPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!Uri.TryCreate(package.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || package.SizeBytes <= 0 || package.SizeBytes > MaximumArchiveBytes
            || package.Sha256.Length != 64 || !package.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("WWM package metadata is invalid.");

        var workDir = Path.Combine(_paths.CacheDir, "packages", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var archivePath = Path.Combine(workDir, "package.zip");
        var stageDir = Path.Combine(workDir, "staged");
        try
        {
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("WWM package download redirected away from HTTPS.");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > package.SizeBytes || total > MaximumArchiveBytes)
                        throw new InvalidDataException("WWM package exceeds its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (total != package.SizeBytes) throw new InvalidDataException("WWM package size does not match API metadata.");
            }

            var archiveHash = await HashHelper.ComputeFileSha256Async(archivePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(archiveHash, package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("WWM package SHA-256 does not match API metadata.");

            Directory.CreateDirectory(stageDir);
            var staged = ValidateAndExtract(archivePath, stageDir, cancellationToken);
            return new WwmStagedPackage(workDir, staged, package);
        }
        catch
        {
            Cleanup(workDir);
            throw;
        }
    }

    private static IReadOnlyList<WwmTargetManifestEntry> ValidateAndExtract(string archivePath, string stageDir, CancellationToken token)
    {
        var definition = WwmGameDefinition.Default;
        var expected = definition.ManagedRelativePaths.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<WwmTargetManifestEntry>();
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntries)
            throw new InvalidDataException("WWM archive entry count is outside the allowed bounds.");
        long extractedSize = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var raw = entry.FullName;
            if (raw.EndsWith('/'))
            {
                var normalizedDirectory = NormalizeArchivePath(raw.TrimEnd('/'));
                if (normalizedDirectory is not ("Package" or "Package/HD" or "Package/HD/oversea" or "Package/HD/oversea/locale"))
                    throw new InvalidDataException("WWM archive contains an unexpected directory.");
                continue;
            }
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            var dosAttributes = entry.ExternalAttributes & 0xFFFF;
            if (unixType == 0xA000 || (dosAttributes & 0x400) != 0)
                throw new InvalidDataException("WWM archive contains a link/reparse-like entry.");

            var relative = NormalizeArchivePath(raw);
            if (relative.StartsWith('/') || Path.IsPathRooted(relative) || relative.Contains(':')
                || relative.Split('/').Any(part => part is ".." or "." or ""))
                throw new InvalidDataException("WWM archive contains an unsafe path.");
            var windowsRelative = Normalize(relative);
            if (!expected.Contains(windowsRelative) || !seen.Add(windowsRelative))
                throw new InvalidDataException("WWM archive contains an unexpected or duplicate target.");
            if (entry.Length <= 0 || entry.Length > MaximumExtractedBytes)
                throw new InvalidDataException("WWM archive file length is outside the allowed bounds.");
            extractedSize = checked(extractedSize + entry.Length);
            if (extractedSize > MaximumExtractedBytes) throw new InvalidDataException("WWM extracted size limit exceeded.");

            var destination = Path.GetFullPath(Path.Combine(stageDir, windowsRelative));
            var prefix = Path.GetFullPath(stageDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("WWM archive path escaped staging directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var input = entry.Open())
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    written += read;
                    if (written > entry.Length || written > MaximumExtractedBytes)
                        throw new InvalidDataException("WWM archive entry exceeded its declared extraction size.");
                    output.Write(buffer, 0, read);
                }
            }
            var info = new FileInfo(destination);
            if (info.Length != entry.Length) throw new InvalidDataException("Staged WWM file size mismatch.");
            result.Add(new WwmTargetManifestEntry
            {
                RelativePath = windowsRelative,
                SizeBytes = info.Length,
                Sha256 = HashHelper.ComputeFileSha256(destination)
            });
        }
        if (seen.Count != expected.Count || result.Count != 2)
            throw new InvalidDataException("WWM archive does not contain the complete approved target set.");
        return result.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeArchivePath(string value) => value.Replace('\\', '/');
    private static string Normalize(string value) => value.Replace('/', '\\');

    internal void Cleanup(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception ex) { _logger.Warning($"Could not remove WWM temporary directory '{directory}': {ex.Message}"); }
    }
}

public sealed record WwmStagedPackage(string WorkDirectory, IReadOnlyList<WwmTargetManifestEntry> Targets, WwmApiPackage Package);
