using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class AppNotificationContentTests
{
    [Fact]
    public void ApplicationUpdate_ContainsActionableHubVersion()
    {
        var content = AppNotificationContent.ForApplicationUpdate("v1.2.21");

        Assert.Equal("application_update", content.Type);
        Assert.Equal("v1.2.21", content.Identity);
        Assert.Contains("v1.2.21", content.Title, StringComparison.Ordinal);
        Assert.Contains("оновитися", content.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("black-desert-online", "full-ukrainian", "Українська", "Black Desert Online", "release-bdo-2", "v3.4.0")]
    [InlineData("where-winds-meet", "english-terms/default", "Українська (Всі терміни англійською)", "Where Winds Meet", "release-wwm-2", "v2.6.8")]
    public void LocalizationUpdate_ContainsGameModeAndReleaseIdentity(
        string gameId, string modeKey, string modeName, string gameName, string releaseId, string version)
    {
        var content = AppNotificationContent.ForLocalizationUpdate(
            gameId, modeKey, modeName, gameName, releaseId, version);

        Assert.Equal("localization", content.Type);
        Assert.Equal($"{gameId}/{modeKey}/{releaseId}", content.Identity);
        Assert.Contains(modeName, content.Title, StringComparison.Ordinal);
        Assert.Contains(gameName, content.Body, StringComparison.Ordinal);
        Assert.Contains(version, content.Body, StringComparison.Ordinal);
        Assert.Contains("оновлення", content.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LocalizationUpdate_WithoutVersionDoesNotInventOne(string? version)
    {
        var content = AppNotificationContent.ForLocalizationUpdate(
            "where-winds-meet", "ukrainian/default", "Українська", "Where Winds Meet", "release-1", version!);

        Assert.DoesNotContain("до ", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("невідомо", content.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_RejectsMissingRequiredNotificationFields()
    {
        Assert.Throws<ArgumentException>(() => AppNotificationContent.Create("", "id", "title", "body"));
        Assert.Throws<ArgumentException>(() => AppNotificationContent.Create("type", " ", "title", "body"));
        Assert.Throws<ArgumentException>(() => AppNotificationContent.Create("type", "id", "", "body"));
        Assert.Throws<ArgumentException>(() => AppNotificationContent.Create("type", "id", "title", " "));
    }
}
