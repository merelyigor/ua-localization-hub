namespace BdoClient;

/// <summary>
/// Public artifact identities and intentionally stable local compatibility identities.
/// </summary>
internal static class ApplicationTechnicalIdentity
{
    public const string RepositoryOwner = "merelyigor";
    public const string CanonicalRepositoryName = "ua-localization-hub";
    public const string LegacyRepositoryName = "bdo-ua-client";
    public const string UserAgent = "BDO-UA-Client";
    public const string ExecutableFileName = "BDO-WWM-UAClient.exe";
    public const string LegacyExecutableFileName = "BDO-UA-Client.exe";
    public const string AutostartValueName = "BDO-UA-Client";
    public const string LocalAppDataDirectoryName = "BDO-UA-Client";
    public const string SingleInstanceNamePrefix = "BDO-UA-Client";
    public const string LogFilePrefix = "bdo-ua-client";
    public const string ReplacementWorkspaceDirectoryName = ".bdo-ua-client-update";

    public const string CanonicalRepositorySlug = RepositoryOwner + "/" + CanonicalRepositoryName;
    public const string LegacyRepositorySlug = RepositoryOwner + "/" + LegacyRepositoryName;

    public static string BuildReleasesApiUrl(string repositoryName)
        => $"https://api.github.com/repos/{RepositoryOwner}/{repositoryName}/releases?per_page=100";

    public static string BuildPackageFileName(string version)
        => $"BDO-WWM-UAClient-v{version}-win-x64.zip";

    public static string BuildLegacyPackageFileName(string version)
        => $"BDO-UA-Client-v{version}-win-x64.zip";

    public static string BuildPackageFileName(string executableFileName, string version)
    {
        if (string.Equals(executableFileName, ExecutableFileName, StringComparison.Ordinal))
            return BuildPackageFileName(version);
        if (string.Equals(executableFileName, LegacyExecutableFileName, StringComparison.Ordinal))
            return BuildLegacyPackageFileName(version);
        throw new ArgumentException("Unsupported executable identity", nameof(executableFileName));
    }

    public static bool IsSupportedExecutableFileName(string? fileName)
        => string.Equals(fileName, ExecutableFileName, StringComparison.Ordinal)
            || string.Equals(fileName, LegacyExecutableFileName, StringComparison.Ordinal);
}
