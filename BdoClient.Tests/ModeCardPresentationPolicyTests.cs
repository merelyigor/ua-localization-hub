using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests;

public sealed class ModeCardPresentationPolicyTests
{
    [Fact]
    public void ExactInstalledCard_UsesCompactNeutralReinstallControlAtUpperLeft()
    {
        using var card = new LocalizationModeCard(Mode("full", "A")) { Size = new System.Drawing.Size(320, 240) };
        var presentation = Create(LocalizationState.UpToDate, "full", "A", card.Mode);
        var actions = 0;
        card.ActionRequested += (_, _) => actions++;

        card.ApplyPresentation(presentation);

        Assert.Null(card.ActionTextForTest);
        Assert.True(card.ReinstallVisibleForTest);
        Assert.True(card.ReinstallEnabledForTest);
        Assert.Equal("Перевстановити", card.ReinstallTextForTest);
        Assert.Equal(UiTheme.NeutralText, card.ReinstallButtonForTest.ForeColor);
        Assert.True(card.ReinstallButtonForTest.Left < card.Width / 2);
        Assert.True(card.ReinstallButtonForTest.Top < 60);
        card.ReinstallButtonForTest.PerformClick();
        Assert.Equal(1, actions);
    }

    [Fact]
    public void NothingInstalled_CompatibleMode_OffersInstall()
    {
        var presentation = Create(LocalizationState.NotInstalled, null, null, Mode("full", "A"));
        Assert.Equal("Встановити", presentation.ActionText);
        Assert.True(presentation.ActionEnabled);
        Assert.Null(presentation.StateText);
        Assert.Equal(ModeCardTone.Neutral, presentation.Tone);
    }

    [Fact]
    public void OrdinaryInstallable_HasNoBadge()
    {
        var presentation = Create(LocalizationState.NotInstalled, null, null, Mode("full", "A"));
        Assert.Null(presentation.StateText);
        Assert.Equal("Встановити", presentation.ActionText);
        Assert.True(presentation.ActionEnabled);
        Assert.False(presentation.IsInstalled);
        Assert.False(presentation.IsBusy);
    }

    [Fact]
    public void OrdinaryInstallable_DifferentMode_HasNoBadge()
    {
        var presentation = Create(LocalizationState.UpToDate, "full", "A", Mode("other", "B"));
        Assert.Null(presentation.StateText);
        Assert.Equal("Встановити", presentation.ActionText);
        Assert.True(presentation.ActionEnabled);
    }

    [Fact]
    public void Operation_NonTarget_Ordinary_HasNoBadge()
    {
        var presentation = Create(LocalizationState.NotInstalled, null, null, Mode("full", "A"), operation: true, activeTarget: false);
        Assert.Null(presentation.StateText);
        Assert.False(presentation.IsBusy);
        Assert.False(presentation.ActionEnabled);
    }

    [Fact]
    public void ExactTarget_UpToDate_ShowsInstalledWithSeparateSecondaryReinstall()
    {
        var presentation = Create(LocalizationState.UpToDate, "full", "A", Mode("full", "A"));
        Assert.True(presentation.IsInstalled);
        Assert.Equal("✓ Встановлено", presentation.StateText);
        Assert.Null(presentation.ActionText);
        Assert.False(presentation.ActionEnabled);
        Assert.True(presentation.ShowReinstall);
        Assert.True(presentation.ReinstallEnabled);
    }

    [Fact]
    public void ExactTarget_ReinstallIsDisabledWhenWritesAreNotAllowed()
    {
        var presentation = ModeCardPresentationPolicy.Create(
            LocalizationState.UpToDate, "full", "A", Mode("full", "A"), CompatibilityResult.Allowed(),
            operationInProgress: false, activeTarget: false, allowWriteActions: false);

        Assert.Null(presentation.ActionText);
        Assert.True(presentation.ShowReinstall);
        Assert.False(presentation.ReinstallEnabled);
    }

    [Fact]
    public void SameModeNewRelease_OffersUpdate()
    {
        var presentation = Create(LocalizationState.UpdateAvailable, "full", "OLD", Mode("full", "NEW"));
        Assert.Equal("Оновити", presentation.ActionText);
        Assert.True(presentation.ActionEnabled);
    }

    [Fact]
    public void DifferentMode_OffersInstall()
    {
        var presentation = Create(LocalizationState.UpToDate, "full", "A", Mode("other", "B"));
        Assert.Equal("Встановити", presentation.ActionText);
    }

    [Fact]
    public void IncompatibleMode_HasNoAction()
    {
        var presentation = Create(LocalizationState.NotInstalled, null, null, Mode("full", "A"), CompatibilityResult.Blocked("patch mismatch"));
        Assert.Equal(ModeCardTone.Warning, presentation.Tone);
        Assert.False(presentation.ActionEnabled);
        Assert.Null(presentation.ActionText);
    }

    [Fact]
    public void MissingCurrentRelease_HasNoAction()
    {
        var presentation = Create(LocalizationState.NotInstalled, null, null, new LocalizationMode { Slug = "full" });
        Assert.Equal("Реліз недоступний", presentation.StateText);
        Assert.Null(presentation.ActionText);
    }

    [Fact]
    public void WaitingForRelease_UsesWarning()
    {
        var presentation = Create(LocalizationState.WaitingForRelease, "full", "OLD", Mode("full", "NEW"));
        Assert.Equal(ModeCardTone.Warning, presentation.Tone);
    }

    [Fact]
    public void CorruptedMode_IsNeverPresentedAsSuccess()
    {
        var presentation = Create(LocalizationState.Corrupted, "full", "OLD", Mode("full", "NEW"));
        Assert.Equal(ModeCardTone.Error, presentation.Tone);
        Assert.NotEqual("✓ Встановлено", presentation.StateText);
    }

    [Fact]
    public void SameMode_UpdateAvailable_ManagedFileChanged_OffersUpdate()
    {
        var presentation = Create(LocalizationState.UpdateAvailable, "full", "OLD", Mode("full", "NEW"));
        Assert.Equal("Оновити", presentation.ActionText);
        Assert.True(presentation.ActionEnabled);
        Assert.Equal("Доступне оновлення", presentation.StateText);
        Assert.Equal(ModeCardTone.Warning, presentation.Tone);
    }

    [Fact]
    public void SameMode_SamePublicId_UpdateAvailable_OffersUpdate()
    {
        var publicId = "01ABC";
        var presentation = Create(LocalizationState.UpdateAvailable, "full", publicId, Mode("full", publicId));
        Assert.Equal("Оновити", presentation.ActionText);
        Assert.True(presentation.ActionEnabled);
    }

    [Fact]
    public void Operation_DisablesActionsAndMarksTargetBusy()
    {
        var presentation = Create(LocalizationState.NotInstalled, null, null, Mode("full", "A"), operation: true, activeTarget: true);
        Assert.True(presentation.IsBusy);
        Assert.Equal("Виконується операція", presentation.StateText);
        Assert.False(presentation.ActionEnabled);
    }

    [Fact]
    public void Operation_TargetAwaitingConfirmation_ShowsNeutralWaitingStateAndDisablesAction()
    {
        var presentation = ModeCardPresentationPolicy.Create(
            LocalizationState.NotInstalled, null, null, Mode("full", "A"), CompatibilityResult.Allowed(),
            operationInProgress: true, activeTarget: true, awaitingConfirmation: true);

        Assert.Equal("Очікує підтвердження", presentation.StateText);
        Assert.Equal(ModeCardTone.Busy, presentation.Tone);
        Assert.False(presentation.ActionEnabled);
        Assert.Null(presentation.ActionText);
    }

    [Fact]
    public void CachedFeed_ShowsActionButKeepsItDisabled()
    {
        var presentation = ModeCardPresentationPolicy.Create(
            LocalizationState.NotInstalled, null, null, Mode("full", "A"),
            CompatibilityResult.Allowed(), operationInProgress: false,
            activeTarget: false, allowWriteActions: false);

        Assert.Equal("Встановити", presentation.ActionText);
        Assert.False(presentation.ActionEnabled);
    }

    private static ModeCardPresentation Create(LocalizationState state, string? installedSlug, string? installedId, LocalizationMode mode, CompatibilityResult? compatibility = null, bool operation = false, bool activeTarget = false, bool awaitingConfirmation = false) =>
        ModeCardPresentationPolicy.Create(state, installedSlug, installedId, mode, compatibility ?? CompatibilityResult.Allowed(), operation, activeTarget, awaitingConfirmation: awaitingConfirmation);

    private static LocalizationMode Mode(string slug, string id) => new()
    {
        Slug = slug,
        PublicName = slug,
        Current = new CurrentRelease { PublicId = id, Version = 1, Patch = 1, DownloadUrl = "https://example.com/file", Sha256 = "abc", SizeBytes = 1, CompatibleWithOfficialPatch = true }
    };
}
