namespace BdoClient.Services;

public sealed class WwmGameDefinition
{
    public static WwmGameDefinition Default { get; } = new();
    public string Id => "where-winds-meet";
    public string DisplayName => "Where Winds Meet";
    public string SteamAppId => "3564740";
    public string ExecutableRelativePath => Path.Combine("Engine", "Binaries", "Win64r", "wwm.exe");
    public string LocaleRelativePath => Path.Combine("Package", "HD", "oversea", "locale");
    public string[] ManagedRelativePaths => new[]
    {
        Path.Combine(LocaleRelativePath, "translate_words_map_en"),
        Path.Combine(LocaleRelativePath, "translate_words_map_en_diff")
    };
    public bool ValidateGameRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var root = Path.GetFullPath(path);
            return File.Exists(Path.Combine(root, ExecutableRelativePath))
                && Directory.Exists(Path.Combine(root, LocaleRelativePath));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
    public string GetBuildMarkerPath(string root) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root))!, "steamapps", $"appmanifest_{SteamAppId}.acf");
}
