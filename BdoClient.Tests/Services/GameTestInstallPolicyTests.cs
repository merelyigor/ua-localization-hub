using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class GameTestInstallPolicyTests
{
    [Fact]
    public void Verified_DoesNotRequireConfirmation()
    {
        Assert.False(GameTestInstallPolicy.RequiresConfirmation(new GameTestInfo { State = "verified" }));
    }

    [Theory]
    [InlineData("known_issues", "Є примітка до цього релізу", "Команда локалізації залишила примітку")]
    [InlineData("unverified", "Реліз ще очікує перевірки в грі", "ще не завершено перевірку в грі")]
    [InlineData(null, "Статус перевірки релізу ще не підтверджено", "немає підтвердженого статусу")]
    [InlineData("future_state", "Статус перевірки релізу ще не підтверджено", "немає підтвердженого статусу")]
    public void NonVerifiedOrUnknown_StateRequiresNeutralConfirmation(
        string? state, string expectedHeading, string expectedSupportingText)
    {
        var gameTest = state == null ? null : new GameTestInfo { State = state };

        Assert.True(GameTestInstallPolicy.RequiresConfirmation(gameTest));
        var presentation = GameTestInstallPolicy.BuildPresentation(gameTest);
        Assert.Equal(expectedHeading, presentation.Heading);
        Assert.Contains(expectedSupportingText, presentation.SupportingText);
        Assert.DoesNotContain("Встановити все одно?", presentation.Heading);
        Assert.DoesNotContain("Встановити все одно?", presentation.SupportingText);
    }

    [Fact]
    public void PresentationIncludesServerLabelAndNoteWithoutRewritingThem()
    {
        var presentation = GameTestInstallPolicy.BuildPresentation(new GameTestInfo
        {
            State = "known_issues",
            Label = "Працює, тест ще чекаємо",
            Note = "Перевірте звук у меню."
        });

        Assert.Equal(new[] { "Працює, тест ще чекаємо", "Перевірте звук у меню." }, presentation.ServerDetails);
        Assert.DoesNotContain("УВАГА", presentation.Heading);
        Assert.DoesNotContain("ПОПЕРЕДЖЕННЯ", presentation.Heading);
    }

    [Fact]
    public void PresentationDeduplicatesEqualServerLabelAndNote()
    {
        var presentation = GameTestInstallPolicy.BuildPresentation(new GameTestInfo
        {
            State = "known_issues",
            Label = "Є примітка",
            Note = "Є примітка"
        });

        Assert.Equal(new[] { "Є примітка" }, presentation.ServerDetails);
    }
}
