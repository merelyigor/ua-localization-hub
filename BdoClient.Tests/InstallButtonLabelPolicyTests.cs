using BdoClient.Services;

namespace BdoClient.Tests;

public class InstallButtonLabelPolicyTests
{
    [Fact]
    public void SameInstalledModeWithNewRelease_UsesUpdate()
    {
        var text = InstallButtonLabelPolicy.GetText(
            LocalizationState.UpdateAvailable,
            hasInstalledApiState: true,
            sameInstalledModeSelected: true,
            exactSelectedTarget: false);

        Assert.Equal("Оновити", text);
    }

    [Fact]
    public void DifferentSelectedMode_UsesInstall()
    {
        var text = InstallButtonLabelPolicy.GetText(
            LocalizationState.UpdateAvailable,
            hasInstalledApiState: true,
            sameInstalledModeSelected: false,
            exactSelectedTarget: false);

        Assert.Equal("Встановити", text);
    }

    [Fact]
    public void NoInstalledMode_UsesInstall()
    {
        var text = InstallButtonLabelPolicy.GetText(
            LocalizationState.NotInstalled,
            hasInstalledApiState: false,
            sameInstalledModeSelected: false,
            exactSelectedTarget: false);

        Assert.Equal("Встановити", text);
    }

    [Fact]
    public void ExactInstalledTarget_UsesReinstallLabel()
    {
        var text = InstallButtonLabelPolicy.GetText(
            LocalizationState.UpToDate,
            hasInstalledApiState: true,
            sameInstalledModeSelected: true,
            exactSelectedTarget: true);

        Assert.Equal("Перевстановити", text);
    }
}
