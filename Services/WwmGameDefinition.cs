using System.Diagnostics;

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

    /// <summary>Normalizes a Windows installation root without resolving links or changing filesystem identity.</summary>
    public static string NormalizeInstallationRoot(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public bool ValidateGameRoot(string? path)
    {
        return TryGetWindowsLayout(path, out _);
    }

    public string[] GetManagedRelativePaths(WwmInstallationLayout layout)
    {
        var locale = layout switch
        {
            WwmInstallationLayout.Package => LocaleRelativePath,
            WwmInstallationLayout.LocalDataPatch => LocalDataPatchLocaleRelativePath,
            _ => throw new InvalidDataException("A single supported WWM target layout is required.")
        };
        return new[]
        {
            Path.Combine(locale, "translate_words_map_en"),
            Path.Combine(locale, "translate_words_map_en_diff")
        };
    }

    public bool IsMutationLayoutSafe(string? path, WwmInstallationLayout targetLayout)
    {
        if (!TryGetWindowsLayout(path, out var available)) return false;
        return targetLayout is WwmInstallationLayout.Package or WwmInstallationLayout.LocalDataPatch
            && available.HasFlag(targetLayout);
    }

    public static string ComputeInstallationFingerprint(string root)
    {
        var executable = Path.Combine(NormalizeInstallationRoot(root), Default.ExecutableRelativePath);
        var info = new FileInfo(executable);
        if (!info.Exists || (File.GetAttributes(executable) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("WWM executable marker is unavailable for installation binding.");
        var identity = $"{info.Length}\n{info.LastWriteTimeUtc.Ticks}\n{FileVersionInfo.GetVersionInfo(executable).FileVersion ?? string.Empty}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    public bool TryGetWindowsLayout(string? path, out WwmInstallationLayout layout)
    {
        layout = WwmInstallationLayout.Unknown;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var root = NormalizeInstallationRoot(path);
            if (!HasNoReparseComponents(root, ExecutableRelativePath)
                || !File.Exists(Path.Combine(root, ExecutableRelativePath)))
                return false;

            if (HasSafeDirectory(root, LocaleRelativePath)) layout |= WwmInstallationLayout.Package;
            if (HasSafeDirectory(root, LocalDataPatchLocaleRelativePath)) layout |= WwmInstallationLayout.LocalDataPatch;
            return layout != WwmInstallationLayout.Unknown;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
            or NotSupportedException or System.Security.SecurityException)
        {
            layout = WwmInstallationLayout.Unknown;
            return false;
        }
    }

    public string GetBuildMarkerPath(string root) => Path.Combine(Path.GetDirectoryName(NormalizeInstallationRoot(root))!, "steamapps", $"appmanifest_{SteamAppId}.acf");

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or System.Security.SecurityException) { return true; }
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

public sealed record WwmInstallationBinding(
    string GameRoot,
    WwmInstallationSource Source,
    WwmInstallationLayout Layout,
    string? BuildId,
    string Fingerprint)
{
    public string? LegacyPersistedRootEvidence { get; init; }
    public bool IsUniqueAutomaticSteamDetection { get; init; }
    public string[] ManagedRelativePaths => WwmGameDefinition.Default.GetManagedRelativePaths(Layout);

    public static WwmInstallationBinding FromDetection(WwmDetectionResult detection)
    {
        if (!detection.IsMutationSupported || detection.GameRoot == null || detection.TargetLayout == WwmInstallationLayout.Unknown)
            throw new InvalidDataException("WWM detection does not identify one supported mutation target layout.");
        return new WwmInstallationBinding(WwmGameDefinition.NormalizeInstallationRoot(detection.GameRoot), detection.Source, detection.TargetLayout,
            detection.BuildId, WwmGameDefinition.ComputeInstallationFingerprint(detection.GameRoot))
        {
            LegacyPersistedRootEvidence = detection.PersistedGamePath,
            IsUniqueAutomaticSteamDetection = detection.IsUniqueAutomaticSteamDetection
        };
    }
}
