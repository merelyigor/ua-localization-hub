using BdoClient;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests;

public sealed class WwmModeCardPresentationTests
{
    [Fact]
    public void ExactCurrentPackage_ShowsInstalledWithoutAction()
    {
        var presentation = WwmModeCardPresentation.Create(true, WwmInstalledStateKind.Current,
            stateTrusted: true, recoveryBlocked: false, gameDetected: true, operationInProgress: false);

        Assert.Equal("✓ Встановлено", presentation.StateText);
        Assert.Null(presentation.ActionText);
        Assert.False(presentation.ActionEnabled);
        Assert.Equal(ModeCardTone.Success, presentation.Tone);
        Assert.True(presentation.IsInstalled);
    }

    [Fact]
    public void ChangedCurrentPackage_ShowsUpdateAction()
    {
        var presentation = WwmModeCardPresentation.Create(true, WwmInstalledStateKind.UpdateAvailable,
            stateTrusted: true, recoveryBlocked: false, gameDetected: true, operationInProgress: false);

        Assert.Equal("Доступне оновлення", presentation.StateText);
        Assert.Equal("Оновити", presentation.ActionText);
        Assert.True(presentation.ActionEnabled);
    }

    [Fact]
    public void DifferentInstallableMode_ShowsInstallAction()
    {
        var presentation = WwmModeCardPresentation.Create(false, WwmInstalledStateKind.UpdateAvailable,
            stateTrusted: true, recoveryBlocked: false, gameDetected: true, operationInProgress: false);

        Assert.Equal("Доступно", presentation.StateText);
        Assert.Equal("Встановити", presentation.ActionText);
        Assert.True(presentation.ActionEnabled);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void UntrustedOrRecoveryBlockedState_HidesMutationAction(bool trusted, bool recoveryBlocked)
    {
        var presentation = WwmModeCardPresentation.Create(false, WwmInstalledStateKind.Unknown,
            stateTrusted: trusted, recoveryBlocked: recoveryBlocked, gameDetected: true, operationInProgress: false);

        Assert.Null(presentation.ActionText);
        Assert.False(presentation.ActionEnabled);
    }

    [Fact]
    public void ModifiedManagedFiles_HideMutationAction()
    {
        var presentation = WwmModeCardPresentation.Create(true, WwmInstalledStateKind.Modified,
            stateTrusted: true, recoveryBlocked: false, gameDetected: true, operationInProgress: false);

        Assert.Equal("Файли змінені", presentation.StateText);
        Assert.Null(presentation.ActionText);
        Assert.False(presentation.ActionEnabled);
    }
}
