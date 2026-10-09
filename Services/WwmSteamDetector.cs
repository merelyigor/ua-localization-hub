using System.Text.RegularExpressions;
using BdoClient.Logging;

namespace BdoClient.Services;

public sealed class WwmSteamDetector
{
    private readonly ILogger _logger;
    private readonly IReadOnlyList<string>? _steamRootsOverride;
    private readonly WwmGameDefinition _definition;

    public WwmSteamDetector(ILogger logger, IEnumerable<string>? steamRoots = null, WwmGameDefinition? definition = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _steamRootsOverride = steamRoots?.ToList();
        _definition = definition ?? WwmGameDefinition.Default;
    }

    public WwmDetectionResult Detect()
    {
        foreach (var steamRoot in GetSteamRoots())
        {
            foreach (var library in GetLibraries(steamRoot))
            {
                var manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{_definition.SteamAppId}.acf");
                var manifest = ReadManifest(manifestPath);
                if (manifest == null || manifest.AppId != _definition.SteamAppId
                    || string.IsNullOrWhiteSpace(manifest.InstallDir))
                    continue;

                var common = Path.GetFullPath(Path.Combine(library, "steamapps", "common"));
                var candidate = Path.GetFullPath(Path.Combine(common, manifest.InstallDir));
                if (!IsWithin(candidate, common) || !_definition.ValidateGameRoot(candidate))
                    continue;
                return new WwmDetectionResult(candidate, manifest.BuildId, manifestPath);
            }
        }
        return new WwmDetectionResult(null, null, null);
    }

    public WwmDetectionResult Detect(string? savedRoot)
    {
        if (_definition.ValidateGameRoot(savedRoot))
            return new WwmDetectionResult(Path.GetFullPath(savedRoot!), ReadBuildIdForRoot(savedRoot!), "saved-config");
        return Detect();
    }

    public string? ReadBuildIdForRoot(string gameRoot)
    {
        foreach (var library in GetSteamRoots().SelectMany(GetLibraries))
        {
            var manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{_definition.SteamAppId}.acf");
            var manifest = ReadManifest(manifestPath);
            if (manifest?.AppId != _definition.SteamAppId || manifest.BuildId == null) continue;
            var candidate = Path.GetFullPath(Path.Combine(library, "steamapps", "common", manifest.InstallDir));
            if (string.Equals(candidate, Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase))
                return manifest.BuildId;
        }
        return null;
    }

    public bool TryResolveManualRoot(string selectedPath, out string? gameRoot)
    {
        gameRoot = null;
        if (string.IsNullOrWhiteSpace(selectedPath)) return false;
        try
        {
            var full = Path.GetFullPath(selectedPath);
            if (_definition.ValidateGameRoot(full))
            {
                gameRoot = full;
                return true;
            }
            var children = Directory.GetDirectories(full).Where(_definition.ValidateGameRoot).ToArray();
            if (children.Length == 1)
            {
                gameRoot = Path.GetFullPath(children[0]);
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.Debug($"WWM manual path validation failed: {ex.Message}");
        }
        return false;
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
}

public sealed record WwmSteamManifest(string AppId, string InstallDir, string? BuildId);
public sealed record WwmDetectionResult(string? GameRoot, string? BuildId, string? ManifestPath)
{
    public bool IsFound => GameRoot != null;
}
