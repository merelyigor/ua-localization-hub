using BdoClient.Models;

namespace BdoClient.Services;

internal static class GameTestInstallPolicy
{
    public static bool RequiresConfirmation(GameTestInfo? gameTest) =>
        !string.Equals(gameTest?.State, "verified", StringComparison.Ordinal);

    public static string BuildConfirmationMessage(GameTestInfo? gameTest)
    {
        var message = gameTest?.State switch
        {
            "known_issues" => "Цей реліз має відомі зауваження після перевірки в грі. Встановити все одно?",
            "unverified" => "Цей реліз ще не підтверджено перевіркою в грі. Встановити все одно?",
            _ => "Для цього релізу немає підтвердженого статусу перевірки в грі. Встановити все одно?"
        };

        var details = new[] { gameTest?.Label, gameTest?.Note }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return details.Length == 0
            ? message
            : $"{string.Join(Environment.NewLine, details)}{Environment.NewLine}{Environment.NewLine}{message}";
    }
}
