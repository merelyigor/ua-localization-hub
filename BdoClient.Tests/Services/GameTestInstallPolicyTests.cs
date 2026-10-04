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
    [InlineData("known_issues", "Цей реліз має відомі зауваження")]
    [InlineData("unverified", "ще не підтверджено")]
    [InlineData(null, "немає підтвердженого статусу")]
    [InlineData("future_state", "немає підтвердженого статусу")]
    public void NonVerifiedOrUnknown_StateRequiresConfirmation(string? state, string expectedMessage)
    {
        var gameTest = state == null ? null : new GameTestInfo { State = state };

        Assert.True(GameTestInstallPolicy.RequiresConfirmation(gameTest));
        Assert.Contains(expectedMessage, GameTestInstallPolicy.BuildConfirmationMessage(gameTest));
    }

    [Fact]
    public void ConfirmationMessageIncludesServerLabelAndNote()
    {
        var message = GameTestInstallPolicy.BuildConfirmationMessage(new GameTestInfo
        {
            State = "known_issues",
            Label = "Відомі проблеми",
            Note = "Перевірте звук у меню."
        });

        Assert.Contains("Відомі проблеми", message);
        Assert.Contains("Перевірте звук у меню.", message);
    }
}
