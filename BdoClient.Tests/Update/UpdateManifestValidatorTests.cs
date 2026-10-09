using BdoClient.Logging;
using BdoClient.Update;
using System.Text.Json;

namespace BdoClient.Tests.Update;

public class UpdateManifestValidatorTests
{
    private static readonly ILogger Logger = new NullLogger();

    private static UpdateManifest ValidManifest() => new()
    {
        SchemaVersion = 1,
        Version = "0.1.4",
        Tag = "v0.1.4",
        CommitSha = "74875dfcc6762ec0edb75c40e225150f94fa45e5",
        AssetName = "BDO-WWM-UAClient.exe",
        Sha256 = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
        Platform = "win-x64",
        WorkflowRunId = "32211040254"
    };

    private static UpdateCandidate MakeCandidate(string tag, AppVersion version)
    {
        return new UpdateCandidate(version, tag, new GitHubRelease { TagName = tag });
    }

    [Fact]
    public void ValidManifest_Passes()
    {
        var validator = new UpdateManifestValidator(Logger);
        var result = validator.Validate(ValidManifest(), MakeCandidate("v0.1.4", new AppVersion(0, 1, 4)));
        Assert.True(result.IsValid);
        Assert.NotNull(result.NormalizedSha256);
    }

    [Fact]
    public void OptionalPackageFields_Valid()
    {
        var manifest = ValidManifest();
        manifest.PackageName = "BDO-WWM-UAClient-v0.1.4-win-x64.zip";
        manifest.PackageSha256 = new string('b', 64);
        Assert.True(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OnlyOneOptionalPackageField_Fails(bool hasName, bool hasSha)
    {
        var manifest = ValidManifest();
        manifest.PackageName = hasName ? "BDO-WWM-UAClient-v0.1.4-win-x64.zip" : null;
        manifest.PackageSha256 = hasSha ? new string('b', 64) : null;
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void MalformedPackageSha_Fails()
    {
        var manifest = ValidManifest();
        manifest.PackageName = "BDO-WWM-UAClient-v0.1.4-win-x64.zip";
        manifest.PackageSha256 = "not-a-sha";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void LegacyShapeDeserializer_IgnoresOptionalPackageFields()
    {
        const string json = "{\"schema_version\":1,\"version\":\"0.1.4\",\"tag\":\"v0.1.4\",\"commit_sha\":\"74875dfcc6762ec0edb75c40e225150f94fa45e5\",\"asset_name\":\"BDO-UA-Client.exe\",\"sha256\":\"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2\",\"package_name\":\"BDO-UA-Client-v0.1.4-win-x64.zip\",\"package_sha256\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"platform\":\"win-x64\",\"workflow_run_id\":\"1\"}";
        var legacy = JsonSerializer.Deserialize<LegacyManifest>(json);
        Assert.NotNull(legacy);
        Assert.Equal("BDO-UA-Client.exe", legacy!.AssetName);
        Assert.Equal("0.1.4", legacy.Version);
    }

    private sealed class LegacyManifest
    {
        public int schema_version { get; set; }
        public string? version { get; set; }
        public string? tag { get; set; }
        public string? commit_sha { get; set; }
        public string? asset_name { get; set; }
        public string? sha256 { get; set; }
        public string? platform { get; set; }
        public string? workflow_run_id { get; set; }
        public string? AssetName => asset_name;
        public string? Version => version;
    }

    [Fact]
    public void WrongSchema_Fails()
    {
        var manifest = ValidManifest();
        manifest.SchemaVersion = 2;
        var result = new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4)));
        Assert.False(result.IsValid);
        Assert.Contains("schema_version", result.ErrorMessage);
    }

    [Fact]
    public void MissingVersion_Fails()
    {
        var manifest = ValidManifest();
        manifest.Version = null;
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void VersionMismatch_Fails()
    {
        var manifest = ValidManifest();
        manifest.Version = "0.1.5";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void TagMismatch_Fails()
    {
        var manifest = ValidManifest();
        manifest.Tag = "v0.1.5";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void UpperCaseVTag_Fails()
    {
        var manifest = ValidManifest();
        manifest.Tag = "V0.1.4";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void WrongPlatform_Fails()
    {
        var manifest = ValidManifest();
        manifest.Platform = "linux-x64";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void MissingAssetName_Fails()
    {
        var manifest = ValidManifest();
        manifest.AssetName = null;
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void NonCanonicalAssetName_Fails()
    {
        var manifest = ValidManifest();
        manifest.AssetName = "BDO-UA-Client-v0.1.4-win-x64.zip";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void LegacyManifestAndPackageIdentity_RemainsValid()
    {
        var manifest = ValidManifest();
        manifest.AssetName = ApplicationTechnicalIdentity.LegacyExecutableFileName;
        manifest.PackageName = ApplicationTechnicalIdentity.BuildLegacyPackageFileName("0.1.4");
        manifest.PackageSha256 = new string('b', 64);

        Assert.True(new UpdateManifestValidator(Logger)
            .Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void InvalidShaLength_Fails()
    {
        var manifest = ValidManifest();
        manifest.Sha256 = "abc123";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void InvalidShaChars_Fails()
    {
        var manifest = ValidManifest();
        manifest.Sha256 = new string('z', 64);
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void MissingCommitSha_Fails()
    {
        var manifest = ValidManifest();
        manifest.CommitSha = null;
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void MalformedCommitSha_Fails()
    {
        var manifest = ValidManifest();
        manifest.CommitSha = "not-hex";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void ShaNormalizedToLower()
    {
        var manifest = ValidManifest();
        manifest.Sha256 = manifest.Sha256!.ToUpperInvariant();
        var result = new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4)));
        Assert.True(result.IsValid);
        Assert.Equal(manifest.Sha256.ToLowerInvariant(), result.NormalizedSha256);
    }

    [Fact]
    public void StringWorkflowRunId_Passes()
    {
        var manifest = ValidManifest();
        Assert.True(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void MissingWorkflowRunId_Fails()
    {
        var manifest = ValidManifest();
        manifest.WorkflowRunId = null;
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void EmptyWorkflowRunId_Fails()
    {
        var manifest = ValidManifest();
        manifest.WorkflowRunId = "";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void NonNumericWorkflowRunId_Fails()
    {
        var manifest = ValidManifest();
        manifest.WorkflowRunId = "abc123";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void ZeroWorkflowRunId_Fails()
    {
        var manifest = ValidManifest();
        manifest.WorkflowRunId = "0";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    [Fact]
    public void NegativeWorkflowRunId_Fails()
    {
        var manifest = ValidManifest();
        manifest.WorkflowRunId = "-1";
        Assert.False(new UpdateManifestValidator(Logger).Validate(manifest, MakeCandidate("v0.1.4", new AppVersion(0, 1, 4))).IsValid);
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
