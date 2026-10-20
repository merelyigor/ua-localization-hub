using BdoClient.Services;

namespace BdoClient;

internal static class InstallButtonLabelPolicy
{
    public const string InstallText = "Встановити";
    public const string UpdateText = "Оновити";
    public const string ReinstallText = "Перевстановити";

    public static string GetText(
        LocalizationState factualState,
        bool hasInstalledApiState,
        bool sameInstalledModeSelected,
        bool exactSelectedTarget)
    {
        if (factualState == LocalizationState.UpToDate
            && hasInstalledApiState
            && sameInstalledModeSelected
            && exactSelectedTarget)
            return ReinstallText;

        return factualState == LocalizationState.UpdateAvailable
            && hasInstalledApiState
            && sameInstalledModeSelected
            && !exactSelectedTarget
            ? UpdateText
            : InstallText;
    }
}
