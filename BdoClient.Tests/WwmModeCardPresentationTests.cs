using BdoClient;
using BdoClient.Models;
using BdoClient.Services;
using System.Threading;

namespace BdoClient.Tests;

public sealed class WwmModeCardPresentationTests
{
    [Fact]
    public void ActionButtonClick_RequestsActionOnceWithoutTriggeringSelection()
    {
        var selectionCount = 0;
        var actionCount = 0;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var card = new WwmModeCard(new WwmMode
                {
                    Slug = "ukrainian",
                    Variant = "default",
                    Label = "Українська",
                    Available = true
                });
                card.Present(WwmModeCardPresentation.Create(false, WwmInstalledStateKind.Unknown,
                    stateTrusted: true, recoveryBlocked: false, gameDetected: true, operationInProgress: false));
                card.SelectionRequested += (_, _) => selectionCount++;
                card.ActionRequested += (_, _) => actionCount++;
                card.ActionButtonForTest.PerformClick();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Card interaction test did not finish.");

        Assert.Null(error);
        Assert.Equal(0, selectionCount);
        Assert.Equal(1, actionCount);
    }

    [Fact]
    public void ReinstallButtonClick_RequestsActionOnceWithoutTriggeringSelection()
    {
        var selectionCount = 0;
        var actionCount = 0;
        using var card = new WwmModeCard(new WwmMode
        {
            Slug = "ukrainian",
            Variant = "default",
            Label = "Українська",
            Available = true
        });
        card.Present(WwmModeCardPresentation.Create(true, WwmInstalledStateKind.Current,
            stateTrusted: true, recoveryBlocked: false, gameDetected: true, operationInProgress: false));
        card.SelectionRequested += (_, _) => selectionCount++;
        card.ActionRequested += (_, _) => actionCount++;

        card.ReinstallButtonForTest.PerformClick();

        Assert.Equal(0, selectionCount);
        Assert.Equal(1, actionCount);
    }

    [Fact]
    public void BusyInteractionGate_DisablesActionAndRestoresLatestPresentation()
    {
        using var card = new WwmModeCard(new WwmMode
        {
            Slug = "ukrainian",
            Variant = "default",
            Label = "Українська",
            Available = true
        });
        var available = WwmModeCardPresentation.Create(false, WwmInstalledStateKind.Unknown,
            stateTrusted: true, recoveryBlocked: false, gameDetected: true, operationInProgress: false);
        card.Present(available);

        card.SetInteractionEnabled(false);
        Assert.False(card.ActionEnabledForTest);
        card.PresentBusy("Завантажуємо та встановлюємо реліз…");
        Assert.False(card.ActionEnabledForTest);

        card.Present(available);
        Assert.False(card.ActionEnabledForTest);
        card.SetInteractionEnabled(true);

        Assert.True(card.ActionEnabledForTest);
        Assert.Equal("Встановити", card.ActionTextForTest);
    }

    [Fact]
    public void ExactCurrentPackage_ShowsInstalledWithSecondaryReinstall()
    {
        var presentation = WwmModeCardPresentation.Create(true, WwmInstalledStateKind.Current,
            stateTrusted: true, recoveryBlocked: false, gameDetected: true, operationInProgress: false);

        Assert.Equal("✓ Встановлено", presentation.StateText);
        Assert.Null(presentation.ActionText);
        Assert.False(presentation.ActionEnabled);
        Assert.True(presentation.ShowReinstall);
        Assert.True(presentation.ReinstallEnabled);
        Assert.Equal(ModeCardTone.Success, presentation.Tone);
        Assert.True(presentation.IsInstalled);
    }

    [Fact]
    public void ExactCurrentPackage_LeavesSecondaryReinstallVisibleButDisabledDuringOperation()
    {
        var presentation = WwmModeCardPresentation.Create(true, WwmInstalledStateKind.Current,
            stateTrusted: true, recoveryBlocked: false, gameDetected: true, operationInProgress: true);

        Assert.Null(presentation.ActionText);
        Assert.False(presentation.ActionEnabled);
        Assert.True(presentation.ShowReinstall);
        Assert.False(presentation.ReinstallEnabled);
        Assert.True(presentation.IsInstalled);
    }

    [Fact]
    public void ExactCurrentCard_ShowsQuietSeparateReinstallButtonAndNoPrimaryAction()
    {
        using var card = new WwmModeCard(new WwmMode
        {
            Slug = "ukrainian",
            Variant = "default",
            Label = "Українська",
            Available = true
        });
        card.Present(WwmModeCardPresentation.Create(true, WwmInstalledStateKind.Current,
            stateTrusted: true, recoveryBlocked: false, gameDetected: true, operationInProgress: false));

        Assert.False(card.ActionVisibleForTest);
        Assert.True(card.ReinstallVisibleForTest);
        Assert.True(card.ReinstallEnabledForTest);
        Assert.Equal("Перевстановити", card.ReinstallTextForTest);
        Assert.Equal(UiTheme.NeutralText, card.ReinstallButtonForTest.ForeColor);
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

    [Fact]
    public void ExternallyChangedManagedFiles_OfferReinstallWithoutRestoreClaim()
    {
        var presentation = WwmModeCardPresentation.Create(true, WwmInstalledStateKind.NeedsReinstall,
            stateTrusted: true, recoveryBlocked: false, gameDetected: true, operationInProgress: false);

        Assert.Equal("Файли гри змінено — потрібне повторне встановлення", presentation.StateText);
        Assert.Equal("Встановити", presentation.ActionText);
        Assert.True(presentation.ActionEnabled);
        Assert.False(presentation.IsInstalled);
    }
}
