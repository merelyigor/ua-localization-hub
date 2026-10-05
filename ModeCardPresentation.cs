using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient;

internal enum ModeCardTone { Neutral, Success, Warning, Error, Busy }

internal sealed record ModeCardPresentation(
    string? StateText,
    ModeCardTone Tone,
    string? ActionText,
    bool ActionEnabled,
    bool IsInstalled,
    bool IsBusy,
    string? DetailText);

internal static class ModeCardPresentationPolicy
{
    public static ModeCardPresentation Create(
        LocalizationState factualState,
        string? installedModeSlug,
        string? installedPublicId,
        LocalizationMode mode,
        CompatibilityResult compatibility,
        bool operationInProgress,
        bool activeTarget,
        bool allowWriteActions = true,
        bool awaitingConfirmation = false)
    {
        var current = mode.Current;
        if (current == null)
            return new("Реліз недоступний", ModeCardTone.Warning, null, false, false, false, null);

        var exact = InstallActionPolicy.IsExactInstalledTarget(installedModeSlug, installedPublicId, mode);
        var sameMode = !string.IsNullOrWhiteSpace(installedModeSlug)
            && string.Equals(installedModeSlug, mode.Slug, StringComparison.Ordinal);
        var policy = InstallActionPolicy.Evaluate(
            factualState, installedModeSlug, installedPublicId, mode, current,
            compatibility, operationInProgress, allowWriteActions);

        if (operationInProgress)
        {
            return new(activeTarget
                    ? awaitingConfirmation ? "Очікує підтвердження" : "Виконується операція"
                    : StateTextForExisting(exact, sameMode, factualState),
                activeTarget ? ModeCardTone.Busy : exact ? ModeCardTone.Success : ModeCardTone.Neutral,
                activeTarget ? null : ActionTextFor(sameMode, factualState), false, exact, activeTarget, null);
        }

        if (!compatibility.IsAllowed)
            return new("Несумісно", ModeCardTone.Warning, null, false, exact, false, compatibility.Reason);

        if (exact && factualState == LocalizationState.UpToDate)
            return new("✓ Встановлено", ModeCardTone.Success, null, false, true, false, "Поточний реліз встановлено");

        if (sameMode && factualState == LocalizationState.UpdateAvailable)
            return new("Доступне оновлення", ModeCardTone.Warning,
                InstallButtonLabelPolicy.UpdateText, policy.CanInstall, false, false, "Для встановленого режиму є новий реліз");

        if (sameMode && factualState is LocalizationState.Corrupted or LocalizationState.WaitingForRelease or LocalizationState.InstalledVersionUnknown)
            return new(LocalizationStatePresentation.GetDisplayText(new LocalizationStateResult(factualState, null, null)),
                factualState == LocalizationState.Corrupted ? ModeCardTone.Error : ModeCardTone.Warning,
                policy.CanInstall ? InstallButtonLabelPolicy.InstallText : null, policy.CanInstall, false, false, null);

        return new(null, ModeCardTone.Neutral, InstallButtonLabelPolicy.InstallText,
            policy.CanInstall, false, false, null);
    }

    private static string? StateTextForExisting(bool exact, bool sameMode, LocalizationState state) =>
        exact ? "✓ Встановлено" : sameMode && state == LocalizationState.UpdateAvailable ? "Доступне оновлення" : null;

    private static string? ActionTextFor(bool sameMode, LocalizationState state) =>
        sameMode && state == LocalizationState.UpdateAvailable ? InstallButtonLabelPolicy.UpdateText : InstallButtonLabelPolicy.InstallText;
}
