using System.Text.RegularExpressions;
using BdoClient.Logging;

namespace BdoClient.Services;

public sealed class WwmSteamDetector
{
    internal const int DefaultMaximumManualDirectories = 20_000;
    internal const int DefaultMaximumManualDepth = 32;
    private readonly ILogger _logger;
    private readonly IReadOnlyList<string>? _steamRootsOverride;
    private readonly WwmGameDefinition _definition;
    private readonly Func<string, IEnumerable<string>> _enumerateDirectories;

    public WwmSteamDetector(ILogger logger, IEnumerable<string>? steamRoots = null, WwmGameDefinition? definition = null)
        : this(logger, steamRoots, definition, path => Directory.EnumerateDirectories(path),
            DefaultMaximumManualDirectories, DefaultMaximumManualDepth)
    {
    }

    internal WwmSteamDetector(
        ILogger logger,
        IEnumerable<string>? steamRoots,
        WwmGameDefinition? definition,
        Func<string, IEnumerable<string>> enumerateDirectories,
        int maximumManualDirectories = DefaultMaximumManualDirectories,
        int maximumManualDepth = DefaultMaximumManualDepth)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _steamRootsOverride = steamRoots?.ToList();
        _definition = definition ?? WwmGameDefinition.Default;
        _enumerateDirectories = enumerateDirectories ?? throw new ArgumentNullException(nameof(enumerateDirectories));
        if (maximumManualDirectories <= 0) throw new ArgumentOutOfRangeException(nameof(maximumManualDirectories));
        if (maximumManualDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumManualDepth));
        _maximumManualDirectories = maximumManualDirectories;
        _maximumManualDepth = maximumManualDepth;
    }

    private readonly int _maximumManualDirectories;
    private readonly int _maximumManualDepth;

    public WwmDetectionResult Detect()
    {
        foreach (var steamRoot in GetSteamRoots())
        {
            foreach (var library in GetLibraries(steamRoot))
            {
                var manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{_definition.SteamAppId}.acf");
                var manifest = ReadManifest(manifestPath);
                if (!IsUsableManifest(manifest)) continue;

                var common = Path.GetFullPath(Path.Combine(library, "steamapps", "common"));
                var candidate = Path.GetFullPath(Path.Combine(common, manifest!.InstallDir));
                if (!IsWithin(candidate, common) || !_definition.TryGetWindowsLayout(candidate, out var layout)
                    || !layout.HasFlag(WwmInstallationLayout.Package))
                    continue;
                return new WwmDetectionResult(candidate, WwmInstallationSource.Steam, layout,
                    manifest.BuildId, Path.GetFullPath(manifestPath));
            }
        }
        return WwmDetectionResult.NotFound;
    }

    public WwmDetectionResult Detect(string? savedRoot)
    {
        if (TryDescribeRoot(savedRoot, "saved-config", out var savedDetection))
            return savedDetection;
        return Detect();
    }

    public WwmDetectionResult DescribeRoot(string gameRoot, string evidenceMarker)
    {
        if (TryDescribeRoot(gameRoot, evidenceMarker, out var result)) return result;
        return WwmDetectionResult.NotFound;
    }

    public string? ReadBuildIdForRoot(string gameRoot)
    {
        return TryGetSteamManifestForRoot(gameRoot, out var manifest, out _)
            ? manifest?.BuildId
            : null;
    }

    public ManualResolveResult ResolveManualRoot(string selectedPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(selectedPath)) return ManualResolveResult.NotFound();
        string selected;
        try { selected = Path.GetFullPath(selectedPath); }
        catch (Exception ex) when (IsPathException(ex))
        {
            _logger.Debug($"WWM manual path is invalid: {ex.Message}");
            return ManualResolveResult.NotFound();
        }

        if (TryGetLayout(selected, out _)) return ManualResolveResult.Found(selected);
        if (!Directory.Exists(selected) || IsReparsePoint(selected)) return ManualResolveResult.NotFound();

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string Path, int Depth)>();
        var visited = 0;
        var inspectedDirectories = 0;
        if (!TryGetChildDirectories(selected, _maximumManualDirectories, cancellationToken,
                out var selectedChildren, out var inspected))
            return ManualResolveResult.Incomplete();
        inspectedDirectories += inspected;
        foreach (var child in selectedChildren.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            pending.Push((child, 1));

        var incomplete = false;
        while (pending.Count > 0 && visited < _maximumManualDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            visited++;
            if (IsReparsePoint(directory)) continue;

            if (TryGetLayout(directory, out _))
            {
                try { candidates.Add(Path.GetFullPath(directory)); }
                catch (Exception ex) when (IsPathException(ex)) { _logger.Debug($"WWM candidate path is invalid: {ex.Message}"); }
                continue;
            }

            if (depth >= _maximumManualDepth)
            {
                if (!TryGetChildDirectories(directory, _maximumManualDirectories - inspectedDirectories,
                        cancellationToken, out var deeperChildren, out inspected)
                    || deeperChildren.Length > 0)
                    incomplete = true;
                inspectedDirectories += inspected;
                if (incomplete) break;
                continue;
            }

            var availableSlots = _maximumManualDirectories - inspectedDirectories;
            if (!TryGetChildDirectories(directory, availableSlots, cancellationToken, out var children, out inspected))
            {
                incomplete = true;
                break;
            }
            inspectedDirectories += inspected;
            foreach (var child in children.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                pending.Push((child, depth + 1));
        }

        if (pending.Count > 0) incomplete = true;
        if (incomplete)
        {
            _logger.Warning("WWM manual search could not inspect the complete selected tree.");
            return ManualResolveResult.Incomplete();
        }
        if (candidates.Count == 1) return ManualResolveResult.Found(candidates.Single());
        if (candidates.Count > 1) return ManualResolveResult.Ambiguous();
        return ManualResolveResult.NotFound();
    }

    public bool TryResolveManualRoot(string selectedPath, out string? gameRoot)
    {
        var result = ResolveManualRoot(selectedPath);
        gameRoot = result.Status == ManualResolveStatus.Found ? result.GamePath : null;
        return result.Status == ManualResolveStatus.Found;
    }

    internal static WwmSteamManifest? ParseManifest(string text)
    {
        var appId = Regex.Match(text, "\\\"appid\\\"\\s+\\\"(?<v>[^\\\"]+)\\\"", RegexOptions.IgnoreCase).Groups["v"].Value;
        var installDir = Regex.Match(text, "\\\"installdir\\\"\\s+\\\"(?<v>[^\\\"]+)\\\"", RegexOptions.IgnoreCase).Groups["v"].Value;
        var buildId = Regex.Match(text, "\\\"buildid\\\"\\s+\\\"(?<v>[^\\\"]+)\\\"", RegexOptions.IgnoreCase).Groups["v"].Value;
        return string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(installDir)
            ? null
            : new WwmSteamManifest(appId, installDir, string.IsNullOrEmpty(buildId) ? null : buildId);
    }

    private bool TryDescribeRoot(string? path, string evidenceMarker, out WwmDetectionResult result)
    {
        result = WwmDetectionResult.NotFound;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!TryGetLayout(fullPath, out var layout)) return false;
            if (TryGetSteamManifestForRoot(fullPath, out var manifest, out var manifestPath))
            {
                result = new WwmDetectionResult(fullPath, WwmInstallationSource.Steam, layout,
                    manifest!.BuildId, manifestPath);
                return true;
            }

            result = new WwmDetectionResult(fullPath, WwmInstallationSource.UnknownWindows,
                layout, null, evidenceMarker);
            return true;
        }
        catch (Exception ex) when (IsPathException(ex))
        {
            _logger.Debug($"WWM root validation failed: {ex.Message}");
            return false;
        }
    }

    private bool TryGetLayout(string path, out WwmInstallationLayout layout)
        => _definition.TryGetWindowsLayout(path, out layout);

    private bool TryGetSteamManifestForRoot(string gameRoot, out WwmSteamManifest? matchingManifest, out string? manifestPath)
    {
        matchingManifest = null;
        manifestPath = null;
        string normalizedRoot;
        try { normalizedRoot = Path.GetFullPath(gameRoot); }
        catch (Exception ex) when (IsPathException(ex)) { return false; }

        foreach (var library in GetSteamRoots().SelectMany(GetLibraries))
        {
            var candidateManifestPath = Path.Combine(library, "steamapps", $"appmanifest_{_definition.SteamAppId}.acf");
            var manifest = ReadManifest(candidateManifestPath);
            if (!IsUsableManifest(manifest)) continue;
            try
            {
                var common = Path.GetFullPath(Path.Combine(library, "steamapps", "common"));
                var candidate = Path.GetFullPath(Path.Combine(common, manifest!.InstallDir));
                if (!IsWithin(candidate, common) || !string.Equals(candidate, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    continue;
                matchingManifest = manifest;
                manifestPath = Path.GetFullPath(candidateManifestPath);
                return true;
            }
            catch (Exception ex) when (IsPathException(ex))
            {
                _logger.Debug($"WWM Steam manifest path is invalid: {ex.Message}");
            }
        }
        return false;
    }

    private bool IsUsableManifest(WwmSteamManifest? manifest) => manifest != null
        && manifest.AppId == _definition.SteamAppId
        && !string.IsNullOrWhiteSpace(manifest.InstallDir)
        && !Path.IsPathRooted(manifest.InstallDir);

    private bool TryGetChildDirectories(
        string path,
        int maximumEntries,
        CancellationToken cancellationToken,
        out string[] children,
        out int inspectedEntries)
    {
        var result = new List<string>();
        inspectedEntries = 0;
        try
        {
            foreach (var directory in _enumerateDirectories(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (inspectedEntries >= maximumEntries)
                {
                    children = result.ToArray();
                    inspectedEntries++;
                    return false;
                }
                inspectedEntries++;
                if (IsReparsePoint(directory)) continue;
                result.Add(directory);
            }
            children = result.OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase).ToArray();
            return true;
        }
        catch (Exception ex) when (IsPathException(ex))
        {
            _logger.Debug($"WWM manual search skipped inaccessible directory: {ex.Message}");
            children = result.ToArray();
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (Exception ex) when (IsPathException(ex)) { return true; }
    }

    private WwmSteamManifest? ReadManifest(string path)
    {
        try { return File.Exists(path) ? ParseManifest(File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Debug($"WWM Steam manifest could not be read: {ex.Message}");
            return null;
        }
    }

    private IEnumerable<string> GetSteamRoots()
    {
        if (_steamRootsOverride != null)
            return _steamRootsOverride.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            var path = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(path)) roots.Add(path.Replace('/', '\\'));
        }
        catch (Exception ex) { _logger.Debug($"Steam registry lookup failed: {ex.Message}"); }
        foreach (var candidate in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
            if (Directory.Exists(candidate)) roots.Add(candidate);
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetLibraries(string steamRoot)
    {
        var libraries = new List<string> { Path.GetFullPath(steamRoot) };
        var file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(file))
        {
            try
            {
                var content = File.ReadAllText(file);
                var matches = Regex.Matches(content, "\\\"path\\\"\\s+\\\"(?<p>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    var path = match.Groups["p"].Value.Replace("\\\\", "\\").Replace('/', '\\');
                    if (Path.IsPathRooted(path)) libraries.Add(Path.GetFullPath(path));
                }
                var legacyMatches = Regex.Matches(content, "\\\"\\d+\\\"\\s+\\\"(?<p>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                foreach (Match match in legacyMatches)
                {
                    var path = match.Groups["p"].Value.Replace("\\\\", "\\").Replace('/', '\\');
                    if (Path.IsPathRooted(path)) libraries.Add(Path.GetFullPath(path));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Debug($"WWM Steam library list could not be read: {ex.Message}");
            }
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsWithin(string candidate, string parent)
    {
        var prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathException(Exception exception) => exception is
        ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException;
}

public sealed record WwmSteamManifest(string AppId, string InstallDir, string? BuildId);

public sealed record WwmDetectionResult(
    string? GameRoot,
    WwmInstallationSource Source,
    WwmInstallationLayout Layout,
    string? BuildId,
    string? EvidenceMarker)
{
    public static WwmDetectionResult NotFound { get; } = new(null, WwmInstallationSource.UnknownWindows,
        WwmInstallationLayout.Unknown, null, null);
    public bool IsFound => GameRoot != null;
    public bool IsMutationSupported => IsFound && Source == WwmInstallationSource.Steam
        && Layout.HasFlag(WwmInstallationLayout.Package);
    public string? ManifestPath => Source == WwmInstallationSource.Steam ? EvidenceMarker : null;
}
