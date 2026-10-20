using BdoClient.Models;

namespace BdoClient.Services;

internal sealed class InstallActionPolicyResult
{
    public bool CanInstall { get; }
    public bool CanRestoreOriginal { get; }
    public bool AlreadyInstalledExactTarget { get; }

    public InstallActionPolicyResult(bool canInstall, bool canRestoreOriginal, bool alreadyInstalledExactTarget)
    {
        CanInstall = canInstall;
        CanRestoreOriginal = canRestoreOriginal;
        AlreadyInstalledExactTarget = alreadyInstalledExactTarget;
    }
}

internal static class InstallActionPolicy
{
    public static bool IsExactInstalledTarget(
        string? installedModeSlug,
        string? installedPublicId,
        LocalizationMode? mode)
    {
        if (installedModeSlug == null || installedPublicId == null) return false;
        if (mode?.Slug == null || mode.Current?.PublicId == null) return false;

        return string.Equals(installedModeSlug, mode.Slug, StringComparison.Ordinal)
            && string.Equals(installedPublicId, mode.Current.PublicId, StringComparison.Ordinal);
    }

    public static InstallActionPolicyResult Evaluate(
        LocalizationState factualState,
        string? installedModeSlug,
        string? installedPublicId,
        LocalizationMode? selectedMode,
        CurrentRelease? selectedCurrent,
        CompatibilityResult compatResult,
        bool operationInProgress,
        bool allowWriteActions = true)
    {
        bool alreadyInstalled = false;
        if (factualState == LocalizationState.UpToDate
            && installedModeSlug != null
            && selectedMode?.Slug != null
            && selectedCurrent?.PublicId != null
            && string.Equals(installedModeSlug, selectedMode.Slug, StringComparison.Ordinal)
            && string.Equals(installedPublicId, selectedCurrent.PublicId, StringComparison.Ordinal))
        {
            alreadyInstalled = true;
        }

        bool structurallyValid = selectedCurrent != null
            && DynamicModePolicy.IsStructurallyInstallable(selectedMode!);

        var canInstall = allowWriteActions
            && !operationInProgress
            && structurallyValid
            && compatResult.IsAllowed;

        var canRestoreOriginal = allowWriteActions
            && !operationInProgress
            && factualState is LocalizationState.UpToDate
                or LocalizationState.UpdateAvailable
                or LocalizationState.WaitingForRelease
                or LocalizationState.Corrupted
                or LocalizationState.InstalledVersionUnknown;

        return new InstallActionPolicyResult(canInstall, canRestoreOriginal, alreadyInstalled);
    }
}
