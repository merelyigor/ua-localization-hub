using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public class InstallActionPolicyTests
{
    private static LocalizationMode MakeMode(string slug, string? publicName = null, CurrentRelease? current = null)
    {
        return new LocalizationMode { Slug = slug, PublicName = publicName, Current = current };
    }

    private static CurrentRelease MakeCurrent(string publicId = "01ABC", int version = 1, int patch = 100,
        string? downloadUrl = "https://example.com/release.loc", string? sha256 = "abc123", long sizeBytes = 1000,
        bool compatible = true, string? publishedAt = null)
    {
        return new CurrentRelease
        {
            PublicId = publicId,
            Version = version,
            Patch = patch,
            DownloadUrl = downloadUrl,
            Sha256 = sha256,
            SizeBytes = sizeBytes,
            CompatibleWithOfficialPatch = compatible,
            PublishedAt = publishedAt
        };
    }

    [Fact]
    public void NothingInstalled_ValidCompatible_CanInstall()
    {
        var selectedMode = MakeMode("full-ukrainian", current: MakeCurrent());
        var selectedCurrent = MakeCurrent();

        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.NotInstalled, null, null,
            selectedMode, selectedCurrent, CompatibilityResult.Allowed(), false);

        Assert.True(policy.CanInstall);
        Assert.False(policy.AlreadyInstalledExactTarget);
    }

    [Fact]
    public void SameMode_SamePublicId_UpToDate_AllowsExplicitReinstall()
    {
        var publicId = "01ABC";
        var selectedMode = MakeMode("english-items", current: MakeCurrent(publicId: publicId));
        var selectedCurrent = MakeCurrent(publicId: publicId);

        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.UpToDate, "english-items", publicId,
            selectedMode, selectedCurrent, CompatibilityResult.Allowed(), false);

        Assert.True(policy.CanInstall);
        Assert.True(policy.AlreadyInstalledExactTarget);
    }

    [Fact]
    public void SameMode_DifferentPublicId_CanInstall()
    {
        var selectedMode = MakeMode("english-items", current: MakeCurrent(publicId: "01NEW"));
        var selectedCurrent = MakeCurrent(publicId: "01NEW");

        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.UpdateAvailable, "english-items", "01OLD",
            selectedMode, selectedCurrent, CompatibilityResult.Allowed(), false);

        Assert.True(policy.CanInstall);
        Assert.False(policy.AlreadyInstalledExactTarget);
    }

    [Fact]
    public void SameMode_SamePublicId_UpdateAvailable_CanInstall()
    {
        var publicId = "01ABC";
        var selectedMode = MakeMode("full-ukrainian", current: MakeCurrent(publicId: publicId));
        var selectedCurrent = MakeCurrent(publicId: publicId);

        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.UpdateAvailable, "full-ukrainian", publicId,
            selectedMode, selectedCurrent, CompatibilityResult.Allowed(), false);

        Assert.True(policy.CanInstall);
        Assert.False(policy.AlreadyInstalledExactTarget);
    }

    [Fact]
    public void DifferentInstalledMode_CanInstall()
    {
        var selectedMode = MakeMode("full-ukrainian", current: MakeCurrent());
        var selectedCurrent = MakeCurrent();

        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.UpToDate, "english-items", "01ABC",
            selectedMode, selectedCurrent, CompatibilityResult.Allowed(), false);

        Assert.True(policy.CanInstall);
        Assert.False(policy.AlreadyInstalledExactTarget);
    }

    [Fact]
    public void Incompatible_CanInstallFalse()
    {
        var selectedMode = MakeMode("full-ukrainian", current: MakeCurrent());
        var selectedCurrent = MakeCurrent();

        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.NotInstalled, null, null,
            selectedMode, selectedCurrent, CompatibilityResult.Blocked("patch mismatch"), false);

        Assert.False(policy.CanInstall);
    }

    [Fact]
    public void CachedFeed_DisablesAllWriteActions()
    {
        var selectedMode = MakeMode("full-ukrainian", current: MakeCurrent());
        var selectedCurrent = selectedMode.Current;

        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.UpdateAvailable, "full-ukrainian", "01OLD",
            selectedMode, selectedCurrent, CompatibilityResult.Allowed(), false,
            allowWriteActions: false);

        Assert.False(policy.CanInstall);
        Assert.False(policy.CanRestoreOriginal);
    }

    [Fact]
    public void SelectedMalformed_CanInstallFalse()
    {
        var selectedMode = MakeMode("", current: MakeCurrent(publicId: ""));
        var selectedCurrent = MakeCurrent(publicId: "");

        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.NotInstalled, null, null,
            selectedMode, selectedCurrent, CompatibilityResult.Allowed(), false);

        Assert.False(policy.CanInstall);
    }

    [Fact]
    public void OperationActive_AllDisabled()
    {
        var selectedMode = MakeMode("full-ukrainian", current: MakeCurrent());
        var selectedCurrent = MakeCurrent();

        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.NotInstalled, null, null,
            selectedMode, selectedCurrent, CompatibilityResult.Allowed(), true);

        Assert.False(policy.CanInstall);
        Assert.False(policy.CanRestoreOriginal);
    }

    [Fact]
    public void Corrupted_RestoreOriginalTrue()
    {
        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.Corrupted, null, null,
            null, null, CompatibilityResult.Allowed(), false);

        Assert.True(policy.CanRestoreOriginal);
    }

    [Fact]
    public void InstalledVersionUnknown_RestoreOriginalTrue()
    {
        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.InstalledVersionUnknown, null, null,
            null, null, CompatibilityResult.Allowed(), false);

        Assert.True(policy.CanRestoreOriginal);
    }

    [Fact]
    public void NotInstalled_RestoreOriginalFalse()
    {
        var policy = InstallActionPolicy.Evaluate(
            LocalizationState.NotInstalled, null, null,
            null, null, CompatibilityResult.Allowed(), false);

        Assert.False(policy.CanRestoreOriginal);
    }

    // --- IsExactInstalledTarget ---

    [Fact]
    public void IsExactInstalled_SameSlugSamePublicId_True()
    {
        var mode = MakeMode("english-items", current: MakeCurrent(publicId: "01ABC"));
        Assert.True(InstallActionPolicy.IsExactInstalledTarget("english-items", "01ABC", mode));
    }

    [Fact]
    public void IsExactInstalled_SameSlugDifferentPublicId_False()
    {
        var mode = MakeMode("english-items", current: MakeCurrent(publicId: "01NEW"));
        Assert.False(InstallActionPolicy.IsExactInstalledTarget("english-items", "01OLD", mode));
    }

    [Fact]
    public void IsExactInstalled_DifferentSlugSamePublicId_False()
    {
        var mode = MakeMode("full-ukrainian", current: MakeCurrent(publicId: "01ABC"));
        Assert.False(InstallActionPolicy.IsExactInstalledTarget("english-items", "01ABC", mode));
    }

    [Fact]
    public void IsExactInstalled_NullInputs_False()
    {
        var mode = MakeMode("english-items", current: MakeCurrent(publicId: "01ABC"));
        Assert.False(InstallActionPolicy.IsExactInstalledTarget(null, "01ABC", mode));
        Assert.False(InstallActionPolicy.IsExactInstalledTarget("english-items", null, mode));
        Assert.False(InstallActionPolicy.IsExactInstalledTarget("english-items", "01ABC", null));
    }

    [Fact]
    public void IsExactInstalled_BlankInputs_False()
    {
        var mode = MakeMode("english-items", current: MakeCurrent(publicId: "01ABC"));
        Assert.False(InstallActionPolicy.IsExactInstalledTarget("", "01ABC", mode));
        Assert.False(InstallActionPolicy.IsExactInstalledTarget("english-items", "", mode));
    }

    [Fact]
    public void IsExactInstalled_ModeCurrentNull_False()
    {
        var mode = MakeMode("english-items", current: null);
        Assert.False(InstallActionPolicy.IsExactInstalledTarget("english-items", "01ABC", mode));
    }
}
