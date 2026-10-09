using BdoClient.Logging;

namespace BdoClient.Update;

public sealed class UpdateCandidate
{
    public AppVersion Version { get; }
    public string TagName { get; }
    public GitHubRelease Release { get; }
    public string? BundleAssetName { get; }

    public UpdateCandidate(AppVersion version, string tagName, GitHubRelease release, string? bundleAssetName = null)
    {
        Version = version;
        TagName = tagName;
        Release = release;
        BundleAssetName = bundleAssetName;
    }
}

public sealed class UpdateSelectionPolicy
{
    private const string ManifestAssetName = "release-manifest.json";

    private readonly ILogger _logger;

    public UpdateSelectionPolicy(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public UpdateCandidate? FindUpdate(
        AppVersionInfo currentVersionInfo,
        List<GitHubRelease> releases)
    {
        if (!currentVersionInfo.IsPublicRelease || !currentVersionInfo.PublicVersion.HasValue)
        {
            _logger.Debug("Update: current version is not a public release; updater disabled");
            return null;
        }

        var currentVersion = currentVersionInfo.PublicVersion.Value;
        var currentTag = $"v{currentVersion}";

        var currentRelease = releases.FirstOrDefault(r =>
            !r.Draft &&
            r.PublishedAt.HasValue &&
            string.Equals(r.TagName, currentTag, StringComparison.Ordinal));

        if (currentRelease == null)
        {
            _logger.Debug($"Update: current release {currentTag} not found in published releases; updater disabled");
            return null;
        }

        var currentIsPrerelease = currentRelease.Prerelease;

        UpdateCandidate? best = null;

        foreach (var release in releases)
        {
            if (release.Draft) continue;
            if (!release.PublishedAt.HasValue) continue;
            if (release.TagName == null) continue;

            var candidateVersion = AppVersion.TryParseReleaseTag(release.TagName);
            if (!candidateVersion.HasValue) continue;

            if (candidateVersion.Value <= currentVersion) continue;

            if (!currentIsPrerelease && release.Prerelease) continue;

            if (best == null || candidateVersion.Value > best.Version)
            {
                best = new UpdateCandidate(candidateVersion.Value, release.TagName, release);
            }
        }

        if (best == null)
        {
            _logger.Debug("Update: no eligible newer release found");
            return null;
        }

        var manifestAssetCount = best.Release.Assets?.Count(a =>
            string.Equals(a.Name, ManifestAssetName, StringComparison.Ordinal)) ?? 0;
        if (manifestAssetCount > 1)
        {
            _logger.Warning($"Update: candidate {best.TagName} has ambiguous {ManifestAssetName}; fail closed");
            return null;
        }

        if (manifestAssetCount == 1)
        {
            var manifestAsset = best.Release.Assets!.Single(a =>
                string.Equals(a.Name, ManifestAssetName, StringComparison.Ordinal));
            if (!string.Equals(manifestAsset.State, "uploaded", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning($"Update: candidate {best.TagName} has non-uploaded {ManifestAssetName}; fail closed");
                return null;
            }
        }
        else
        {
            var version = best.Version.ToString();
            var canonicalName = ApplicationTechnicalIdentity.BuildPackageFileName(version);
            var legacyName = ApplicationTechnicalIdentity.BuildLegacyPackageFileName(version);
            var canonicalCount = CountAssets(best, canonicalName);
            var legacyCount = CountAssets(best, legacyName);
            var hasDirectExe = best.Release.Assets?.Any(a =>
                ApplicationTechnicalIdentity.IsSupportedExecutableFileName(a.Name)) == true;
            if (canonicalCount > 1 || legacyCount > 1 || hasDirectExe ||
                canonicalCount == 0 && legacyCount == 0)
            {
                _logger.Warning($"Update: candidate {best.TagName} has missing or ambiguous supported bundle assets; fail closed");
                return null;
            }

            foreach (var name in new[] { canonicalName, legacyName })
            {
                if (CountAssets(best, name) != 0 && UpdatePackageService.FindExactlyOneAsset(best, name) == null)
                {
                    _logger.Warning($"Update: candidate {best.TagName} has an invalid bundle asset '{name}'; fail closed");
                    return null;
                }
            }

            best = new UpdateCandidate(best.Version, best.TagName, best.Release,
                canonicalCount == 1 ? canonicalName : legacyName);
        }

        _logger.Debug($"Update: candidate selected: {best.TagName} (prerelease={best.Release.Prerelease})");
        return best;
    }

    private static int CountAssets(UpdateCandidate candidate, string name)
        => candidate.Release.Assets?.Count(a => string.Equals(a.Name, name, StringComparison.Ordinal)) ?? 0;
}
