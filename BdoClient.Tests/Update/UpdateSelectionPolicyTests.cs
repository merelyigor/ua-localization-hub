using BdoClient.Logging;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class UpdateSelectionPolicyTests
{
    private static readonly ILogger Logger = new NullLogger();

    private static readonly DateTimeOffset DefaultPublishedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static GitHubRelease MakeRelease(
        string tag,
        bool draft = false,
        bool prerelease = false,
        bool published = true,
        params (string name, string url, string state)[] assets)
    {
        return new GitHubRelease
        {
            TagName = tag,
            Draft = draft,
            Prerelease = prerelease,
            PublishedAt = published ? DefaultPublishedAt : null,
            Assets = assets.Select(a => new GitHubReleaseAsset
            {
                Name = a.name,
                BrowserDownloadUrl = a.url,
                State = a.state
            }).ToList()
        };
    }

    private static (string name, string url, string state) Manifest =>
        ("release-manifest.json", "https://example.com/manifest", "uploaded");

    [Fact]
    public void CurrentNotPublic_ReturnsNull()
    {
        var info = AppVersionInfo.FromRawVersion("0.0.0-dev.abcdef");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease> { MakeRelease("v0.1.3", assets: Manifest) };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void CurrentNotPublished_ReturnsNull()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease> { MakeRelease("v0.1.4", assets: Manifest) };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void CurrentDraft_ReturnsNull()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", draft: true, assets: Manifest),
            MakeRelease("v0.1.4", assets: Manifest)
        };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void CurrentPrerelease_AllowsNewerPrerelease()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", prerelease: true, assets: Manifest),
            MakeRelease("v0.1.4", prerelease: true, assets: Manifest)
        };
        var candidate = policy.FindUpdate(info, releases);
        Assert.NotNull(candidate);
        Assert.Equal("v0.1.4", candidate!.TagName);
    }

    [Fact]
    public void CurrentPrerelease_AllowsNewerStable()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", prerelease: true, assets: Manifest),
            MakeRelease("v0.1.4", assets: Manifest)
        };
        var candidate = policy.FindUpdate(info, releases);
        Assert.NotNull(candidate);
        Assert.Equal("v0.1.4", candidate!.TagName);
    }

    [Fact]
    public void CurrentStable_IgnoresNewerPrerelease()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: Manifest),
            MakeRelease("v0.1.4", prerelease: true, assets: Manifest)
        };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void CurrentStable_AllowsNewerStable()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: Manifest),
            MakeRelease("v0.1.4", assets: Manifest)
        };
        var candidate = policy.FindUpdate(info, releases);
        Assert.NotNull(candidate);
        Assert.Equal("v0.1.4", candidate!.TagName);
    }

    [Fact]
    public void MultipleCandidates_SelectsHighestNumeric()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: Manifest),
            MakeRelease("v0.1.9", assets: Manifest),
            MakeRelease("v0.1.10", assets: Manifest),
            MakeRelease("v0.1.8", assets: Manifest)
        };
        var candidate = policy.FindUpdate(info, releases);
        Assert.NotNull(candidate);
        Assert.Equal("v0.1.10", candidate!.TagName);
    }

    [Fact]
    public void DraftCandidate_Ignored()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: Manifest),
            MakeRelease("v0.1.1", draft: true, assets: Manifest)
        };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void UnpublishedCandidate_Ignored()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: Manifest),
            MakeRelease("v0.1.1", published: false, assets: Manifest)
        };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void BareTag_Ignored()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: Manifest),
            MakeRelease("0.1.1", assets: Manifest)
        };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void UpperVTag_Ignored()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: Manifest),
            MakeRelease("V0.1.1", assets: Manifest)
        };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void LeadingZeroTag_Ignored()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: Manifest),
            MakeRelease("v01.1.1", assets: Manifest)
        };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void HighestCandidateWithoutManifest_FailClosed()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: Manifest),
            MakeRelease("v0.1.2", assets: ("BDO-UA-Client.exe", "https://example.com/BDO-UA-Client.exe", "uploaded")),
            MakeRelease("v0.1.1", assets: Manifest)
        };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void CanonicalBundleWithoutExternalManifest_IsSelected()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: Manifest),
            new GitHubRelease
            {
                TagName = "v0.1.4",
                PublishedAt = DefaultPublishedAt,
                Assets = new List<GitHubReleaseAsset>
                {
                    new()
                    {
                        Name = "BDO-WWM-UAClient-v0.1.4-win-x64.zip",
                        BrowserDownloadUrl = "https://example.com/bundle.zip",
                        Size = 100,
                        State = "uploaded"
                    }
                }
            }
        };

        var candidate = policy.FindUpdate(info, releases);

        Assert.NotNull(candidate);
        Assert.Equal("v0.1.4", candidate!.TagName);
        Assert.Equal("BDO-WWM-UAClient-v0.1.4-win-x64.zip", candidate.BundleAssetName);
    }

    [Fact]
    public void DualBundleRelease_IsCanonicalForNewClientAndSelectableByV129()
    {
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: Manifest),
            MakeRelease("v0.1.4", assets: new[]
            {
                ("BDO-WWM-UAClient-v0.1.4-win-x64.zip", "https://example.com/canonical.zip", "uploaded"),
                ("BDO-UA-Client-v0.1.4-win-x64.zip", "https://example.com/legacy.zip", "uploaded")
            })
        };
        releases[1].Assets![0].Size = 100;
        releases[1].Assets![1].Size = 100;

        var candidate = policy.FindUpdate(AppVersionInfo.FromRawVersion("0.1.3"), releases);

        Assert.NotNull(candidate);
        Assert.Equal("BDO-WWM-UAClient-v0.1.4-win-x64.zip", candidate!.BundleAssetName);
        Assert.Single(candidate.Release.Assets!, a => a.Name == "BDO-UA-Client-v0.1.4-win-x64.zip");
        Assert.DoesNotContain(candidate.Release.Assets!, a => a.Name == "BDO-UA-Client.exe");
        Assert.NotNull(UpdatePackageService.FindExactlyOneAsset(candidate, "BDO-UA-Client-v0.1.4-win-x64.zip"));
    }

    [Fact]
    public void LegacyBundleWithoutCanonical_IsAcceptedForCompatibility()
    {
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: Manifest),
            MakeRelease("v0.1.4", assets: ("BDO-UA-Client-v0.1.4-win-x64.zip", "https://example.com/legacy.zip", "uploaded"))
        };
        releases[1].Assets![0].Size = 100;

        var candidate = policy.FindUpdate(AppVersionInfo.FromRawVersion("0.1.3"), releases);

        Assert.NotNull(candidate);
        Assert.Equal("BDO-UA-Client-v0.1.4-win-x64.zip", candidate!.BundleAssetName);
    }

    [Fact]
    public void DuplicateCanonicalBundle_FailsClosed()
    {
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: Manifest),
            MakeRelease("v0.1.4", assets: new[]
            {
                ("BDO-WWM-UAClient-v0.1.4-win-x64.zip", "https://example.com/a.zip", "uploaded"),
                ("BDO-WWM-UAClient-v0.1.4-win-x64.zip", "https://example.com/b.zip", "uploaded")
            })
        };
        releases[1].Assets![0].Size = 100;
        releases[1].Assets![1].Size = 100;

        Assert.Null(policy.FindUpdate(AppVersionInfo.FromRawVersion("0.1.3"), releases));
    }

    [Fact]
    public void MalformedLegacyBundleWithCanonicalBundle_FailsClosed()
    {
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: Manifest),
            MakeRelease("v0.1.4", assets: new[]
            {
                ("BDO-WWM-UAClient-v0.1.4-win-x64.zip", "https://example.com/canonical.zip", "uploaded"),
                ("BDO-UA-Client-v0.1.4-win-x64.zip", "http://example.com/legacy.zip", "uploaded")
            })
        };
        releases[1].Assets![0].Size = 100;
        releases[1].Assets![1].Size = 100;

        Assert.Null(policy.FindUpdate(AppVersionInfo.FromRawVersion("0.1.3"), releases));
    }

    [Fact]
    public void BundleWithDirectExeWithoutManifest_FailsClosed()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var release = MakeRelease("v0.1.4", assets: new[]
        {
            ("BDO-WWM-UAClient-v0.1.4-win-x64.zip", "https://example.com/bundle.zip", "uploaded"),
            ("BDO-UA-Client.exe", "https://example.com/app.exe", "uploaded")
        });
        release.Assets!.First(a => a.Name!.EndsWith(".zip", StringComparison.Ordinal)).Size = 100;
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: Manifest),
            release
        };

        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void ManifestNotUploaded_FailClosed()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.0");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.0", assets: Manifest),
            MakeRelease("v0.1.1", assets: ("release-manifest.json", "https://example.com/manifest", "pending"))
        };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    [Fact]
    public void OlderReleasesIgnored()
    {
        var info = AppVersionInfo.FromRawVersion("0.1.3");
        var policy = new UpdateSelectionPolicy(Logger);
        var releases = new List<GitHubRelease>
        {
            MakeRelease("v0.1.3", assets: Manifest),
            MakeRelease("v0.1.1", assets: Manifest),
            MakeRelease("v0.1.0", assets: Manifest)
        };
        Assert.Null(policy.FindUpdate(info, releases));
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
