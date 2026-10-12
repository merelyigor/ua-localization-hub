namespace BdoClient.Services;

public sealed class WwmGameDefinition
{
    public static WwmGameDefinition Default { get; } = new();
    public string Id => "where-winds-meet";
    public string DisplayName => "Where Winds Meet";
    public string SteamAppId => "3564740";
    public string ExecutableRelativePath => Path.Combine("Engine", "Binaries", "Win64r", "wwm.exe");
    public string LocaleRelativePath => Path.Combine("Package", "HD", "oversea", "locale");
    public string LocalDataPatchLocaleRelativePath => Path.Combine("LocalData", "Patch", "HD", "oversea", "locale");
    public string[] ManagedRelativePaths => new[]
    {
        Path.Combine(LocaleRelativePath, "translate_words_map_en"),
        Path.Combine(LocaleRelativePath, "translate_words_map_en_diff")
    };
    public bool ValidateGameRoot(string? path)
    {
        return TryGetWindowsLayout(path, out var layout)
            && layout.HasFlag(WwmInstallationLayout.Package);
    }

    public bool TryGetWindowsLayout(string? path, out WwmInstallationLayout layout)
    {
        layout = WwmInstallationLayout.Unknown;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var root = Path.GetFullPath(path);
            if (!HasNoReparseComponents(root, ExecutableRelativePath)
                || !File.Exists(Path.Combine(root, ExecutableRelativePath)))
                return false;

            if (HasSafeDirectory(root, LocaleRelativePath)) layout |= WwmInstallationLayout.Package;
            if (HasSafeDirectory(root, LocalDataPatchLocaleRelativePath)) layout |= WwmInstallationLayout.LocalDataPatch;
            return layout != WwmInstallationLayout.Unknown;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            layout = WwmInstallationLayout.Unknown;
            return false;
        }
    }

    public string GetBuildMarkerPath(string root) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root))!, "steamapps", $"appmanifest_{SteamAppId}.acf");

    private static bool HasSafeDirectory(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        return HasNoReparseComponents(root, relativePath) && Directory.Exists(path);
    }

    private static bool HasNoReparseComponents(string root, string relativePath)
    {
        var current = root;
        if (IsReparsePoint(current)) return false;
        foreach (var segment in relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsReparsePoint(current)) return false;
        }
        return true;
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}

[Flags]
public enum WwmInstallationLayout
{
    Unknown = 0,
    Package = 1,
    LocalDataPatch = 2
}

public enum WwmInstallationSource
{
    Steam,
    EpicGames,
    OfficialLauncher,
    UnknownWindows
}
