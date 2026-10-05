using BdoClient.Models;

namespace BdoClient.Services;

internal static class GameTestInstallPolicy
{
    public static bool RequiresConfirmation(GameTestInfo? gameTest) =>
        !string.Equals(gameTest?.State, "verified", StringComparison.Ordinal);

    public static GameTestConfirmationPresentation BuildPresentation(GameTestInfo? gameTest)
    {
        var (heading, supportingText) = gameTest?.State switch
        {
            "known_issues" => (
                "Є примітка до цього релізу",
                "Команда локалізації залишила примітку до цього релізу. Ви можете продовжити встановлення або повернутися назад."),
            "unverified" => (
                "Реліз ще очікує перевірки в грі",
                "Для цього релізу ще не завершено перевірку в грі. Ви можете продовжити встановлення або повернутися назад."),
            _ => (
                "Статус перевірки релізу ще не підтверджено",
                "Для цього релізу немає підтвердженого статусу перевірки в грі. Ви можете продовжити встановлення або повернутися назад.")
        };

        var details = new[] { gameTest?.Label, gameTest?.Note }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new GameTestConfirmationPresentation(heading, supportingText, details);
    }
}

internal sealed record GameTestConfirmationPresentation(
    string Heading,
    string SupportingText,
    IReadOnlyList<string> ServerDetails);
