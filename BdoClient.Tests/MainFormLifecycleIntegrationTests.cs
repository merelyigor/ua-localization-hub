using System.Net;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests;

public sealed class MainFormLifecycleIntegrationTests
{
    [Fact]
    public async Task SwitchingSyntheticGame_DrainsOldSessionAndPersistsSelection()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeSyntheticSecondGame: true);

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.GameSelector.Enabled);
        var oldSession = fixture.Form.ActiveGameSession;
        var selectedAfterRequest = await fixture.SelectGameAsync("synthetic-game");
        Assert.Equal("synthetic-game", selectedAfterRequest);
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form =>
            form.SelectedGame.Id == "synthetic-game"
            && form.GameSelector.Text == "Synthetic Game"
            && form.GameSectionCaption == "Synthetic Game"
            && !form.IsSwitchInProgress
            && form.ActivePersistenceRoot == fixture.AppPaths.GetGamePersistencePaths("synthetic-game").Root);
        var config = new ApplicationConfigStore(fixture.AppPaths, new MainFormTestFixture.TestLogger()).Load();
        Assert.Equal("synthetic-game", config.Value!.SelectedGameId);
        Assert.Equal(fixture.AppPaths.GetGamePersistencePaths("synthetic-game").Root, fixture.Form.ActivePersistenceRoot);
        Assert.False(oldSession.ReleaseFeedPoller.IsRunning);
    }

    [Fact]
    public async Task Startup_PersistedSyntheticGameActivatesRegisteredSession()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            applicationConfigJson: "{\"selected_game_id\":\"synthetic-game\"}",
            includeSyntheticSecondGame: true);

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form =>
            form.SelectedGame.Id == "synthetic-game"
            && form.GameSelector.Text == "Synthetic Game"
            && form.GameSectionCaption == "Synthetic Game"
            && !form.IsSwitchInProgress
            && form.ActivePersistenceRoot == fixture.AppPaths.GetGamePersistencePaths("synthetic-game").Root);

        var config = new ApplicationConfigStore(fixture.AppPaths, new MainFormTestFixture.TestLogger()).Load();
        Assert.Equal("synthetic-game", fixture.Form.SelectedGame.Id);
        Assert.Equal("Synthetic Game", fixture.Form.GameSelector.Text);
        Assert.Equal("synthetic-game", config.Value!.SelectedGameId);
        Assert.Equal(fixture.AppPaths.GetGamePersistencePaths("synthetic-game").Root, fixture.Form.ActivePersistenceRoot);
    }

    [Fact]
    public async Task SwitchingSyntheticGameAndBack_RestoresBdoScopeAndSelection()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeSyntheticSecondGame: true);

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.GameSelector.Enabled);

        await fixture.SelectGameAsync("synthetic-game");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form =>
            form.SelectedGame.Id == "synthetic-game"
            && !form.IsSwitchInProgress
            && form.ActivePersistenceRoot == fixture.AppPaths.GetGamePersistencePaths("synthetic-game").Root);

        await fixture.SelectGameAsync("black-desert-online");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form =>
            form.SelectedGame.Id == "black-desert-online"
            && form.GameSelector.Text == "Black Desert Online"
            && form.GameSectionCaption == "Black Desert Online"
            && !form.IsSwitchInProgress
            && form.ActivePersistenceRoot == fixture.AppPaths.GetGamePersistencePaths("black-desert-online").Root);

        var config = new ApplicationConfigStore(fixture.AppPaths, new MainFormTestFixture.TestLogger()).Load();
        Assert.Equal("black-desert-online", config.Value!.SelectedGameId);
        Assert.Equal(fixture.AppPaths.GetGamePersistencePaths("black-desert-online").Root, fixture.Form.ActivePersistenceRoot);
        Assert.DoesNotContain("synthetic-game", fixture.Form.ActivePersistenceRoot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleOldSessionFeedCannotOverwriteNewSession()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeSyntheticSecondGame: true);

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.GameSelector.Enabled);
        var oldSession = fixture.Form.ActiveGameSession;
        var oldGeneration = fixture.Form.GameSessionGeneration;

        await fixture.SelectGameAsync("synthetic-game");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form =>
            form.SelectedGame.Id == "synthetic-game"
            && !form.IsSwitchInProgress
            && form.ActivePersistenceRoot == fixture.AppPaths.GetGamePersistencePaths("synthetic-game").Root);

        await fixture.Form.DeliverSessionFeedForTestAsync(
            oldSession,
            oldGeneration,
            new ReleasesResponse
            {
                Success = true,
                Data = new ReleaseData { OfficialPatch = 999, Modes = new List<LocalizationMode>() }
            });

        await fixture.WaitForAsync(form =>
            form.SelectedGame.Id == "synthetic-game"
            && form.ActivePersistenceRoot == fixture.AppPaths.GetGamePersistencePaths("synthetic-game").Root);
        Assert.Equal("Synthetic Game", fixture.Form.GameSelector.Text);
    }

    [Fact]
    public async Task SelectorIsBlockedDuringOperationAndRecoversAfterwards()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeSyntheticSecondGame: true);

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.GameSelector.Enabled);
        await fixture.SetOperationInProgressAsync(true);
        try
        {
            Assert.False(fixture.Form.GameSelector.Enabled);
            var selected = await fixture.SelectGameAsync("synthetic-game");
            Assert.Equal("black-desert-online", selected);
            Assert.Equal("black-desert-online", fixture.Form.SelectedGame.Id);
            Assert.Equal(fixture.AppPaths.GetGamePersistencePaths("black-desert-online").Root, fixture.Form.ActivePersistenceRoot);

            var config = new ApplicationConfigStore(fixture.AppPaths, new MainFormTestFixture.TestLogger()).Load();
            Assert.Equal("black-desert-online", config.Value!.SelectedGameId);
        }
        finally
        {
            await fixture.SetOperationInProgressAsync(false);
        }

        await fixture.WaitForAsync(form => form.GameSelector.Enabled);
        Assert.False(fixture.Form.IsOperationInProgress);
    }

    [Fact]
    public async Task SwitchingOfflineToGameWithoutScopedCache_DoesNotDisplayBdoCache()
    {
        var handler = MainFormTestFixture.CreateFailureApiHandler(HttpStatusCode.ServiceUnavailable);
        using var fixture = await MainFormTestFixture.StartAsync(
            handler,
            seedReleaseFeedCache: true,
            includeSyntheticSecondGame: true);

        await fixture.WaitForStartupAsync();
        Assert.NotNull(MainFormTestFixture.FindFirstModeCard(fixture.Form));

        await fixture.SelectGameAsync("synthetic-game");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form =>
            form.SelectedGame.Id == "synthetic-game"
            && !form.IsSwitchInProgress);

        Assert.Empty(MainFormTestFixture.FindModeCards(fixture.Form));
        Assert.Equal(
            fixture.AppPaths.GetGamePersistencePaths("synthetic-game").ReleaseFeedCacheFile,
            fixture.Form.ActiveGameSession.ReleaseFeedCacheStore.CacheFile);
    }

    [Fact]
    public async Task Startup_ComposesMainFormAndCompletesWithSavedGame()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler());

        var startup = await fixture.WaitForStartupAsync();

        Assert.True(startup.Form.IsHandleCreated);
        Assert.False(startup.Form.IsDisposed);
        Assert.Equal("✓ Гру знайдено", startup.GameStatus);
        Assert.Equal(fixture.GameRoot, startup.GamePath);
        Assert.True(startup.ApiRequestCount >= 1);
        Assert.True(startup.GitHubRequestCount >= 1);
        Assert.Equal("Хаб Українізаторів BDO - WWM", fixture.Form.Text);
        Assert.Equal("Хаб Українізаторів BDO - WWM", MainFormTestFixture.FindControlText(fixture.Form, text => text == "Хаб Українізаторів BDO - WWM"));
        Assert.Equal("Хаб Українізаторів BDO - WWM", fixture.Form.HeaderTitle);
        Assert.Equal("Українські локалізації для ігор BDO - WWM", fixture.Form.HeaderSubtitle);
        Assert.Equal("Цільові проєкти", fixture.Form.TargetProjectsCaption);
        Assert.Equal("Black Desert Online — BDO UA Translate", fixture.Form.BdoTargetProject);
        Assert.Equal("Доступно", fixture.Form.BdoTargetStatus);
        Assert.Equal("Where Winds Meet — Winds4UA (W4U)", fixture.Form.WwmTargetProject);
        Assert.Equal("Інтеграція готується", fixture.Form.WwmTargetStatus);
        Assert.Equal("Активна гра", fixture.Form.ActiveGameSelectorLabel);
        Assert.Single(fixture.Form.GameSelector.Items.Cast<GameDescriptor>());
        Assert.Equal("Black Desert Online", fixture.Form.GameSelector.Text);
        Assert.Equal("Black Desert Online", fixture.Form.GameSectionCaption);
        Assert.Equal("Як видалити застосунок?", fixture.Form.UninstallHelpText);
        Assert.Equal("Хаб Українізаторів BDO - WWM", fixture.Form.TrayTooltipText);
        Assert.False(fixture.Form.GameSelector.Enabled);
        await fixture.WaitForAsync(form => form.LastContentFitTargetSizeForTest.Height > 0
            && form.ClientSize == form.LastContentFitTargetSizeForTest);
        Assert.True(fixture.Form.ClientSize.Height < fixture.Form.InitialClientHeightForTest,
            "Startup fitting should reduce the actual initial client height when content is shorter.");
    }

    [Fact]
    public async Task ModeSectionHeight_TracksLoadingFailureEmptyAndRebuiltCards()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateModesApiHandler(401, 4),
            gamePatch: 401);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.CountModeCards(form) == 4
            && form.ClientSize == form.LastContentFitTargetSizeForTest);

        var tallSectionHeight = fixture.Form.ModeSectionHeightForTest;
        var tallFormHeight = fixture.Form.ClientSize.Height;
        var cards = MainFormTestFixture.FindModeCards(fixture.Form);
        Assert.NotEqual(cards[0].Bounds.Top, cards[3].Bounds.Top);

        await fixture.ShowModePlaceholderAsync("ShowModeLoadingPlaceholder");
        await fixture.WaitForAsync(form =>
            MainFormTestFixture.FindControlText(form, text => text == "Завантаження доступних режимів...") != null
            && MainFormTestFixture.CountModeCards(form) == 0
            && form.ModeSectionHeightForTest < tallSectionHeight
            && form.ClientSize == form.LastContentFitTargetSizeForTest);
        var compactPlaceholderHeight = fixture.Form.ModeSectionHeightForTest;
        Assert.True(fixture.Form.ClientSize.Height < tallFormHeight);

        await fixture.ApplyFeedCandidateAsync(CreateModesFeed(4));
        await fixture.WaitForAsync(form =>
            MainFormTestFixture.CountModeCards(form) == 4
            && form.ModeSectionHeightForTest > compactPlaceholderHeight
            && form.ClientSize == form.LastContentFitTargetSizeForTest);

        var rebuiltSectionHeight = fixture.Form.ModeSectionHeightForTest;
        await fixture.ShowModePlaceholderAsync("ShowModeFailurePlaceholder");
        await fixture.WaitForAsync(form =>
            MainFormTestFixture.FindControlText(form, text => text == "Не вдалося завантажити режими.") != null
            && MainFormTestFixture.CountModeCards(form) == 0
            && form.ModeSectionHeightForTest < rebuiltSectionHeight
            && form.ClientSize == form.LastContentFitTargetSizeForTest);
        var failurePlaceholderHeight = fixture.Form.ModeSectionHeightForTest;

        await fixture.ApplyFeedCandidateAsync(CreateModesFeed(4));
        await fixture.WaitForAsync(form =>
            MainFormTestFixture.CountModeCards(form) == 4
            && form.ModeSectionHeightForTest > failurePlaceholderHeight
            && form.ClientSize == form.LastContentFitTargetSizeForTest);
        var cardsSectionHeight = fixture.Form.ModeSectionHeightForTest;

        await fixture.ApplyFeedCandidateAsync(CreateModesFeed(0));
        await fixture.WaitForAsync(form =>
            MainFormTestFixture.CountModeCards(form) == 0
            && MainFormTestFixture.FindControlText(form, text =>
                text == "Наразі немає доступних режимів."
                || text.StartsWith("Для патча 401", StringComparison.Ordinal)) != null
            && form.ModeSectionHeightForTest < cardsSectionHeight
            && form.ClientSize == form.LastContentFitTargetSizeForTest);
        var emptyStateHeight = fixture.Form.ModeSectionHeightForTest;

        await fixture.ApplyFeedCandidateAsync(CreateModesFeed(4));
        await fixture.WaitForAsync(form =>
            MainFormTestFixture.CountModeCards(form) == 4
            && form.ModeSectionHeightForTest > emptyStateHeight
            && form.ClientSize == form.LastContentFitTargetSizeForTest);
    }

    private static ReleasesResponse CreateModesFeed(int modeCount)
        => JsonSerializer.Deserialize<ReleasesResponse>(
            MainFormTestFixture.CreateFeedJson(401, modeCount))!;

    [Fact]
    public async Task UserVerticalResize_DoesNotTriggerContentFitLoop()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateModesApiHandler(401, 4),
            gamePatch: 401);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.CountModeCards(form) == 4
            && form.ClientSize == form.LastContentFitTargetSizeForTest);

        var initialHeight = fixture.Form.ClientSize.Height;
        var previousFitTarget = fixture.Form.LastContentFitTargetSizeForTest;
        var resized = await fixture.ResizeVerticallyAndReadAfterLayoutAsync(80);

        Assert.Equal(initialHeight + 80, resized.ClientSize.Height);
        Assert.Equal(previousFitTarget, resized.LastFitTarget);
    }

    [Fact]
    public async Task Startup_UnknownSelectedGameFallsBackAndPreservesApplicationSettings()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            applicationConfigJson: "{\"autostart_prompt_dismissed\":true,\"selected_game_id\":\"removed-game\"}");

        await fixture.WaitForStartupAsync();

        var config = new ApplicationConfigStore(fixture.AppPaths, new MainFormTestFixture.TestLogger()).Load();
        Assert.Equal("black-desert-online", fixture.Form.SelectedGame.Id);
        Assert.Equal("black-desert-online", config.Value!.SelectedGameId);
        Assert.True(config.Value.AutostartPromptDismissed);
    }

    [Fact]
    public async Task Startup_MalformedApplicationConfigDoesNotOverwriteOrBlockBdo()
    {
        const string malformed = "{ malformed";
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            applicationConfigJson: malformed);

        var startup = await fixture.WaitForStartupAsync();

        Assert.Equal("✓ Гру знайдено", startup.GameStatus);
        Assert.Equal(malformed, await File.ReadAllTextAsync(fixture.AppPaths.ApplicationConfigFile));
        Assert.Equal("black-desert-online", fixture.Form.SelectedGame.Id);
    }

    [Fact]
    public async Task Startup_ApiFailureLeavesFormCoherentAndTerminatesCleanly()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateFailureApiHandler(HttpStatusCode.ServiceUnavailable));

        var startup = await fixture.WaitForStartupAsync();

        Assert.True(startup.Form.IsHandleCreated);
        Assert.False(startup.Form.IsDisposed);
        Assert.Equal("✓ Гру знайдено", startup.GameStatus);
        Assert.Equal(fixture.GameRoot, startup.GamePath);
        Assert.Equal("Сервер повернув помилку.", startup.Message);
        Assert.True(startup.GitHubRequestCount >= 1);
    }

    [Fact]
    public async Task Startup_ApiFailureWithValidCache_ShowsCachedFeedAndDisablesWrites()
    {
        var handler = MainFormTestFixture.CreateFailureApiHandler(HttpStatusCode.ServiceUnavailable);
        using var fixture = await MainFormTestFixture.StartAsync(handler, seedReleaseFeedCache: true);

        var startup = await fixture.WaitForStartupAsync();
        var card = MainFormTestFixture.FindFirstModeCard(startup.Form);

        Assert.True(startup.Form.IsHandleCreated);
        Assert.Contains("Сервер недоступний.", startup.Message);
        Assert.Contains("збережені дані", startup.Message);
        Assert.NotNull(card);
        Assert.False(card!.Controls.OfType<Button>().Single().Enabled);
    }

    [Fact]
    public async Task CachedFeedMatchingGamePatchRemainsDisplayOnly()
    {
        var handler = MainFormTestFixture.CreateFailureApiHandler(HttpStatusCode.ServiceUnavailable);
        using var fixture = await MainFormTestFixture.StartAsync(
            handler,
            seedReleaseFeedCache: true,
            gamePatch: 100);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.FindFirstModeCard(form) != null
            && MainFormTestFixture.FindControlText(
                form,
                text => text == "✓ Гру знайдено • patch 100") != null);

        var card = MainFormTestFixture.FindFirstModeCard(fixture.Form);
        Assert.NotNull(card);
        Assert.False(card!.Controls.OfType<Button>().Single().Enabled);
    }

    [Fact]
    public async Task Startup_OutdatedGame_ShowsWarningWithInstalledAndLatestPatches()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(401),
            gamePatch: 399);

        string? status = null;
        await fixture.WaitForAsync(form =>
        {
            status = MainFormTestFixture.FindControlText(
                form, text => text.StartsWith("⚠ Потрібно оновити гру", StringComparison.Ordinal));
            return status != null;
        });

        Assert.Equal(
            $"⚠ Потрібно оновити гру{Environment.NewLine}Встановлено: patch 399 • актуальний: patch 401",
            status);
    }

    [Fact]
    public async Task SecondaryActivationRestoresExistingBackgroundForm()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            startInBackground: true);

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => !form.Visible && form.WindowState == FormWindowState.Minimized);
        var originalForm = fixture.Form;
        fixture.SignalSecondaryActivation();

        await fixture.WaitForAsync(form => form.Visible && form.WindowState == FormWindowState.Normal);

        Assert.Same(originalForm, fixture.Form);
    }

    [Fact]
    public async Task SecondaryActivationRestoresBackgroundFormFromOffscreenBounds()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            startInBackground: true);

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => !form.Visible && form.WindowState == FormWindowState.Minimized);
        await fixture.MoveFormOffScreenAsync();

        fixture.SignalSecondaryActivation();

        await fixture.WaitForAsync(form =>
            form.Visible
            && form.WindowState == FormWindowState.Normal
            && Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(form.Bounds)));

        Assert.False(fixture.Form.IsDisposed);
    }

    [Fact]
    public async Task SecondaryActivationRefreshesGamePatchAfterExternalUpdate()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSingleModeApiHandler(401),
            startInBackground: true,
            gamePatch: 399);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.FindControlText(
                form,
                text => text.StartsWith("⚠ Потрібно оновити гру", StringComparison.Ordinal)) != null);

        var outdatedCard = MainFormTestFixture.FindFirstModeCard(fixture.Form);
        Assert.NotNull(outdatedCard);
        Assert.False(outdatedCard!.Controls.OfType<Button>().Single().Enabled);

        fixture.UpdateGamePatch(401);
        fixture.SignalSecondaryActivation();

        await fixture.WaitForAsync(form =>
            form.Visible
            && MainFormTestFixture.FindControlText(
                form,
                text => text == "✓ Гру знайдено • patch 401") != null);

        var currentCard = MainFormTestFixture.FindFirstModeCard(fixture.Form);
        Assert.NotNull(currentCard);
        Assert.True(currentCard!.Controls.OfType<Button>().Single().Enabled);
        Assert.Null(fixture.HostException);
    }

    [Fact]
    public async Task RepeatedSecondaryActivationDoesNotOverlapGamePatchRefresh()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(401),
            startInBackground: true,
            gamePatch: 399);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.FindControlText(
                form,
                text => text.StartsWith("⚠ Потрібно оновити гру", StringComparison.Ordinal)) != null);

        fixture.UpdateGamePatch(401);
        fixture.SignalSecondaryActivation();
        fixture.SignalSecondaryActivation();

        await fixture.WaitForAsync(form =>
            form.Visible
            && MainFormTestFixture.FindControlText(
                form,
                text => text == "✓ Гру знайдено • patch 401") != null);

        Assert.Null(fixture.HostException);
    }

    [Fact]
    public async Task SingleLocalizationModePreservesMinimumWidthForGlobalStatusUi()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSingleModeApiHandler(401),
            gamePatch: 401);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.FindFirstModeCard(form) != null
            && MainFormTestFixture.FindControlText(
                form,
                text => text == "✓ Гру знайдено • patch 401") != null);

        var card = MainFormTestFixture.FindFirstModeCard(fixture.Form);

        Assert.NotNull(card);
        Assert.True(fixture.Form.ClientSize.Width >= UiTheme.Scale(fixture.Form, 960));
        Assert.True(fixture.Form.MinimumSize.Width >= UiTheme.Scale(fixture.Form, 960));
        Assert.True(card!.Width < fixture.Form.ClientSize.Width);
    }

    [Fact]
    public async Task ThreeLocalizationModesUseCompactMinimumWidth()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateModesApiHandler(401, 3),
            gamePatch: 401);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.CountModeCards(form) == 3
            && MainFormTestFixture.FindControlText(
                form,
                text => text == "✓ Гру знайдено • patch 401") != null);

        Assert.True(fixture.Form.MinimumSize.Width >= UiTheme.Scale(fixture.Form, 820));
        Assert.True(fixture.Form.MinimumSize.Width < UiTheme.Scale(fixture.Form, 960));

        await fixture.SetClientWidthAsync(UiTheme.Scale(fixture.Form, 820));
        await fixture.WaitForAsync(form =>
            form.ClientSize.Width < UiTheme.Scale(form, 960)
            && MainFormTestFixture.CountModeCards(form) == 3);
        AssertThreeCardsFitOneRow(fixture.Form);

        var rebuiltFeed = JsonSerializer.Deserialize<ReleasesResponse>(
            MainFormTestFixture.CreateFeedJson(401, 3))!;
        await fixture.ApplyFeedCandidateAsync(rebuiltFeed);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.CountModeCards(form) == 3
            && form.ClientSize.Width < UiTheme.Scale(form, 960));
        AssertThreeCardsFitOneRow(fixture.Form);
    }

    private static void AssertThreeCardsFitOneRow(MainForm form)
    {
        var cards = MainFormTestFixture.FindModeCards(form);
        Assert.Equal(3, cards.Count);
        Assert.All(cards, card => Assert.True(card.Width >= UiTheme.Scale(card, 240)));
        Assert.Equal(cards[0].Bounds.Top, cards[1].Bounds.Top);
        Assert.Equal(cards[1].Bounds.Top, cards[2].Bounds.Top);
    }

    [Fact]
    public async Task NewerGameThanAvailableLocalizationKeepsWriteActionsDisabled()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSingleModeApiHandler(401),
            gamePatch: 402);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.FindControlText(
                form,
                text => text.StartsWith("⚠ Гра новіша за доступну локалізацію", StringComparison.Ordinal)) != null);

        var card = MainFormTestFixture.FindFirstModeCard(fixture.Form);
        Assert.NotNull(card);
        Assert.False(card!.Controls.OfType<Button>().Single().Enabled);
    }

    [Fact]
    public async Task MalformedGamePatchReadFailsClosedAndSuccessfulRefreshRecovers()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSingleModeApiHandler(401),
            startInBackground: true,
            gamePatch: 401);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.FindControlText(
                form,
                text => text == "✓ Гру знайдено • patch 401") != null);

        fixture.UpdateGamePatchRaw("not-a-patch");
        fixture.SignalSecondaryActivation();

        await fixture.WaitForAsync(form =>
            form.Visible
            && MainFormTestFixture.FindControlText(
                form,
                text => text == "✓ Гру знайдено") != null);

        var failedCard = MainFormTestFixture.FindFirstModeCard(fixture.Form);
        Assert.NotNull(failedCard);
        Assert.False(failedCard!.Controls.OfType<Button>().Single().Enabled);

        fixture.UpdateGamePatch(401);
        fixture.SignalSecondaryActivation();

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.FindControlText(
                form,
                text => text == "✓ Гру знайдено • patch 401") != null);

        var recoveredCard = MainFormTestFixture.FindFirstModeCard(fixture.Form);
        Assert.NotNull(recoveredCard);
        Assert.True(recoveredCard!.Controls.OfType<Button>().Single().Enabled);
    }

    [Fact]
    public async Task LiveFeedPatchChangeRefreshesPresentationAndRuntimeCardLayout()
    {
        var handler = MainFormTestFixture.CreateModesApiHandler(401, 1);
        using var fixture = await MainFormTestFixture.StartAsync(handler, gamePatch: 401);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.CountModeCards(form) == 1
            && MainFormTestFixture.FindControlText(
                form,
                text => text == "✓ Гру знайдено • patch 401") != null);

        var updatedFeed = JsonSerializer.Deserialize<ReleasesResponse>(
            MainFormTestFixture.CreateFeedJson(402, 2))!;
        await fixture.ApplyFeedCandidateAsync(updatedFeed);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.CountModeCards(form) == 2
            && MainFormTestFixture.FindControlText(
                form,
                text => text.StartsWith("⚠ Потрібно оновити гру", StringComparison.Ordinal)) != null);

        Assert.True(fixture.Form.ClientSize.Width >= UiTheme.Scale(fixture.Form, 960));

        var restoredFeed = JsonSerializer.Deserialize<ReleasesResponse>(
            MainFormTestFixture.CreateFeedJson(401, 1))!;
        await fixture.ApplyFeedCandidateAsync(restoredFeed);

        await fixture.WaitForAsync(form =>
            MainFormTestFixture.CountModeCards(form) == 1
            && MainFormTestFixture.FindControlText(
                form,
                text => text == "✓ Гру знайдено • patch 401") != null);

        Assert.True(fixture.Form.ClientSize.Width >= UiTheme.Scale(fixture.Form, 960));
    }

    [Fact]
    public async Task ClosingWhileStartupRequestIsPendingStopsTestHostWithoutOrphanUiThread()
    {
        var handler = MainFormTestFixture.CreatePendingApiHandler();
        using var fixture = await MainFormTestFixture.StartAsync(
            handler,
            exitWhenShown: true);

        await handler.RequestStarted.WaitAsync(MainFormTestFixture.Timeout);
        await fixture.WaitForHostExitAsync();

        Assert.Null(fixture.HostException);
        Assert.False(fixture.IsHostAlive);
    }
}

internal sealed class MainFormTestFixture : IDisposable
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _root;
    private readonly AppPaths _appPaths;
    private readonly SingleInstanceCoordinator _singleInstanceCoordinator;
    private readonly HttpClient _bdoHttpClient;
    private readonly HttpClient _githubHttpClient;
    private readonly MainFormTestHttpHandler _bdoHandler;
    private readonly MainFormTestHttpHandler _githubHandler;
    private readonly string _mutexName;
    private readonly string _eventName;
    private readonly bool _includeSyntheticSecondGame;
    private readonly TaskCompletionSource<MainForm> _formReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<object?> _hostCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Thread _uiThread;
    private Exception? _hostException;
    private MainForm? _form;
    private BdoGameSession? _bdoSession;
    private SelectedGameSessionHost? _sessionHost;
    private bool _disposed;

    private MainFormTestFixture(
        MainFormTestHttpHandler bdoHandler,
        bool startInBackground,
        bool exitWhenShown,
        bool seedReleaseFeedCache,
        int? gamePatch,
        string? applicationConfigJson,
        bool includeSyntheticSecondGame)
    {
        _bdoHandler = bdoHandler;
        _includeSyntheticSecondGame = includeSyntheticSecondGame;
        _githubHandler = CreateSuccessfulGitHubHandler();
        _root = Path.Combine(Path.GetTempPath(), "bdo-ua-mainform-tests", Guid.NewGuid().ToString("N"));
        _appPaths = new AppPaths(Path.Combine(_root, "appdata"));
        _appPaths.EnsureDirectories();

        if (applicationConfigJson != null)
            File.WriteAllText(_appPaths.ApplicationConfigFile, applicationConfigJson);

        if (seedReleaseFeedCache)
        {
            var cacheStore = new ReleaseFeedCacheStore(
                _appPaths.GetGamePersistencePaths("black-desert-online"),
                new TestLogger());
            cacheStore.SaveAsync(CreateCachedFeed(),
                new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))
                .GetAwaiter().GetResult();
        }

        GameRoot = Path.Combine(_root, "fake-game");
        Directory.CreateDirectory(Path.Combine(GameRoot, "ads"));
        File.WriteAllBytes(BdoGameDefinition.Default.GetLocalizationFilePath(GameRoot), Array.Empty<byte>());
        if (gamePatch is > 0)
            File.WriteAllText(Path.Combine(GameRoot, "ads_files"), $"languagedata_en.loc\t{gamePatch.Value}\n");
        File.WriteAllText(
            _appPaths.ConfigFile,
            JsonSerializer.Serialize(new Config { GamePath = GameRoot }));

        if (includeSyntheticSecondGame)
        {
            var syntheticPaths = _appPaths.GetGamePersistencePaths("synthetic-game");
            syntheticPaths.EnsureDirectories();
            File.WriteAllText(
                syntheticPaths.ConfigFile,
                JsonSerializer.Serialize(new Config { GamePath = GameRoot }));
        }

        _bdoHttpClient = new HttpClient(_bdoHandler);
        _githubHttpClient = new HttpClient(_githubHandler);

        _mutexName = $@"Local\BdoClient.Tests.{Guid.NewGuid():N}.Mutex";
        _eventName = $@"Local\BdoClient.Tests.{Guid.NewGuid():N}.Activate";
        _singleInstanceCoordinator = new SingleInstanceCoordinator(_mutexName, _eventName);

        _uiThread = new Thread(() => RunUiLoop(startInBackground, exitWhenShown))
        {
            IsBackground = false,
            Name = "BdoClient.MainFormLifecycleTest"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
    }

    internal string GameRoot { get; }

    internal AppPaths AppPaths => _appPaths;

    internal MainForm Form => _form ?? throw new InvalidOperationException("MainForm is not ready.");

    internal Exception? HostException => _hostException;

    internal bool IsHostAlive => _uiThread.IsAlive;

    internal void UpdateGamePatch(int patch)
    {
        File.WriteAllText(Path.Combine(GameRoot, "ads_files"), $"languagedata_en.loc\t{patch}\n");
    }

    internal void UpdateGamePatchRaw(string content)
    {
        File.WriteAllText(Path.Combine(GameRoot, "ads_files"), content);
    }

    internal async Task ApplyFeedCandidateAsync(ReleasesResponse candidate)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(async () =>
        {
            try
            {
                var apply = typeof(MainForm).GetMethod(
                    "ApplyFeedPipelineAsync",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var task = (Task<bool>)apply!.Invoke(Form, new object[] { candidate })!;
                Assert.True(await task);
                completion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        await completion.Task.WaitAsync(Timeout);
    }

    internal async Task ShowModePlaceholderAsync(string methodName)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(() =>
        {
            try
            {
                var method = typeof(MainForm).GetMethod(
                    methodName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(method);
                method!.Invoke(Form, null);
                completion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        await completion.Task.WaitAsync(Timeout);
    }

    internal async Task SetClientWidthAsync(int width)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(() =>
        {
            try
            {
                Form.ClientSize = new Size(width, Form.ClientSize.Height);
                completion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        await completion.Task.WaitAsync(Timeout);
    }

    internal async Task<(Size ClientSize, Size LastFitTarget)> ResizeVerticallyAndReadAfterLayoutAsync(int delta)
    {
        var completion = new TaskCompletionSource<(Size, Size)>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(() =>
        {
            try
            {
                Form.ClientSize = new Size(Form.ClientSize.Width, Form.ClientSize.Height + delta);
                Form.BeginInvoke(new Action(() => completion.TrySetResult(
                    (Form.ClientSize, Form.LastContentFitTargetSizeForTest))));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return await completion.Task.WaitAsync(Timeout);
    }

    internal async Task<string?> SelectGameAsync(string id)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(() =>
        {
            try
            {
                Form.GameSelector.SelectedValue = id;
                completion.TrySetResult(Form.GameSelector.SelectedValue as string);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return await completion.Task.WaitAsync(Timeout);
    }

    internal async Task SetOperationInProgressAsync(bool value)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(() =>
        {
            try
            {
                Form.SetOperationInProgressForTest(value);
                completion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        await completion.Task.WaitAsync(Timeout);
    }

    internal static async Task<MainFormTestFixture> StartAsync(
        MainFormTestHttpHandler bdoHandler,
        bool startInBackground = false,
        bool exitWhenShown = false,
        bool seedReleaseFeedCache = false,
        int? gamePatch = null,
        string? applicationConfigJson = null,
        bool includeSyntheticSecondGame = false)
    {
        var fixture = new MainFormTestFixture(
            bdoHandler, startInBackground, exitWhenShown, seedReleaseFeedCache, gamePatch, applicationConfigJson,
            includeSyntheticSecondGame);
        fixture._uiThread.Start();

        try
        {
            await fixture._formReady.Task.WaitAsync(Timeout);
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    internal static MainFormTestHttpHandler CreateSuccessfulApiHandler(int officialPatch = 0)
        => new(HttpStatusCode.OK, $"{{\"success\":true,\"data\":{{\"official_patch\":{officialPatch},\"modes\":[]}}}}");

    internal static MainFormTestHttpHandler CreateSingleModeApiHandler(int officialPatch)
        => new(HttpStatusCode.OK, CreateFeedJson(officialPatch, 1));

    internal static MainFormTestHttpHandler CreateModesApiHandler(int officialPatch, int modeCount)
        => new(HttpStatusCode.OK, CreateFeedJson(officialPatch, modeCount));

    internal static string CreateFeedJson(int officialPatch, int modeCount)
    {
        var modes = string.Join(",", Enumerable.Range(1, modeCount).Select(index =>
            $"{{\"slug\":\"full-ukrainian-{index}\",\"public_name\":\"Повна українська {index}\",\"description\":\"Тестовий режим локалізації\",\"current\":{{\"public_id\":\"01TESTMODE{index}\",\"version\":1,\"filename\":\"languagedata_en.loc\",\"download_url\":\"https://example.com/test{index}.loc\",\"size_bytes\":1,\"sha256\":\"{new string('a', 64)}\",\"patch\":{officialPatch},\"compatible_with_official_patch\":true}}}}"));
        return $"{{\"success\":true,\"data\":{{\"official_patch\":{officialPatch},\"modes\":[{modes}]}}}}";
    }

    internal static MainFormTestHttpHandler CreateSuccessfulGitHubHandler()
        => new(HttpStatusCode.OK, "[]");

    internal static MainFormTestHttpHandler CreateFailureApiHandler(HttpStatusCode statusCode)
        => new(statusCode, string.Empty);

    internal static MainFormTestHttpHandler CreatePendingApiHandler()
        => new(HttpStatusCode.OK, "{\"success\":true,\"data\":{\"modes\":[]}}", waitForRelease: true);

    private static ReleasesResponse CreateCachedFeed() => new()
    {
        Success = true,
        Data = new ReleaseData
        {
            OfficialPatch = 100,
            Modes = new List<LocalizationMode>
            {
                new()
                {
                    Slug = "full-ukrainian",
                    PublicName = "Повна українська",
                    Description = "Cached mode",
                    Current = new CurrentRelease
                    {
                        PublicId = "01CACHED",
                        Version = 1,
                        Patch = 100,
                        DownloadUrl = "https://example.com/cached.loc",
                        SizeBytes = 1024,
                        Sha256 = new string('b', 64),
                        CompatibleWithOfficialPatch = true
                    }
                }
            }
        }
    };

    internal async Task<StartupSnapshot> WaitForStartupAsync()
    {
        await _githubHandler.RequestStarted.WaitAsync(Timeout);

        var snapshot = await WaitForAsync(form =>
        {
            var gameStatus = FindControlText(form, text => text == "✓ Гру знайдено");
            var gamePath = FindControlText(form, text => text == GameRoot);
            var degraded = FindControlText(form, text => text == "Сервер повернув помилку.");
            var cached = FindControlText(form, text => text.StartsWith("Сервер недоступний.", StringComparison.Ordinal));

            var success = gameStatus != null && gamePath != null;
            var failure = success && _bdoHandler.StatusCode != HttpStatusCode.OK
                && (degraded != null || cached != null);
            return success && (_bdoHandler.StatusCode == HttpStatusCode.OK || failure)
                ? new StartupSnapshot(
                    form,
                    gameStatus,
                    gamePath,
                    cached ?? degraded,
                    _bdoHandler.RequestCount,
                    _githubHandler.RequestCount)
                : null;
        });

        return snapshot!;
    }

    internal async Task WaitForAsync(Func<MainForm, bool> predicate)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Check()
        {
            try
            {
                if (_form == null || _form.IsDisposed || _form.Disposing)
                {
                    completion.TrySetException(new InvalidOperationException("MainForm closed before condition was met."));
                    return;
                }

                if (predicate(_form))
                {
                    completion.TrySetResult(null);
                    return;
                }

                _form.BeginInvoke((Action)Check);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        var form = await _formReady.Task.WaitAsync(Timeout);
        await form.WaitForStartupCompletionForTestAsync().WaitAsync(Timeout);
        PostToUi(Check);
        await completion.Task.WaitAsync(Timeout);
    }

    internal async Task WaitForSwitchCompletionAsync()
    {
        var form = await _formReady.Task.WaitAsync(Timeout);
        await form.WaitForSwitchCompletionForTestAsync().WaitAsync(Timeout);
    }

    internal void SignalSecondaryActivation()
    {
        using var secondary = new SingleInstanceCoordinator(_mutexName, _eventName);
        secondary.SignalActivation();
    }

    internal async Task MoveFormOffScreenAsync()
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(() =>
        {
            try
            {
                Form.StartPosition = FormStartPosition.Manual;
                Form.Location = new Point(-32000, -32000);
                completion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        await completion.Task.WaitAsync(Timeout);
    }

    internal async Task WaitForHostExitAsync()
    {
        await _hostCompleted.Task.WaitAsync(Timeout);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _bdoHandler.ReleasePendingRequest();

        if (_uiThread.IsAlive)
        {
            try
            {
                PostToUi(ExitFormForTestHostShutdown);
                if (!_uiThread.Join(Timeout))
                    throw new TimeoutException("MainForm test UI thread did not terminate.");
            }
            catch
            {
                if (_uiThread.IsAlive)
                    _uiThread.Interrupt();
                throw;
            }
        }

        _singleInstanceCoordinator.Dispose();
        _sessionHost?.Dispose();
        _bdoHttpClient.Dispose();
        _githubHttpClient.Dispose();

        if (_hostException != null)
            throw new InvalidOperationException("MainForm test UI thread failed.", _hostException);

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void ExitFormForTestHostShutdown()
    {
        var exit = typeof(MainForm).GetMethod(
            "ExitFromTray",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        exit!.Invoke(Form, null);
    }

    private void RunUiLoop(bool startInBackground, bool exitWhenShown)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var logger = new TestLogger();
            var applicationConfigStore = new ApplicationConfigStore(_appPaths, logger);
            var appVersionInfo = AppVersionInfo.FromRawVersion("1.2.2");
            _bdoSession = BdoGameSession.CreateForTests(
                _appPaths, logger, appVersionInfo, _bdoHttpClient);
            var gameCatalog = _includeSyntheticSecondGame
                ? new GameCatalog(new[]
                {
                    _bdoSession.Descriptor,
                    new GameDescriptor("synthetic-game", "Synthetic Game")
                })
                : GameCatalog.Create(_bdoSession.GameDefinition);
            _sessionHost = new SelectedGameSessionHost(
                _bdoSession,
                descriptor => BdoGameSession.CreateForTests(
                    _appPaths, logger, appVersionInfo, new HttpClient(_bdoHandler), ownsHttpClient: true,
                    descriptor));
            var githubClient = new GitHubUpdateClient(_githubHttpClient, logger);
            var selectionPolicy = new UpdateSelectionPolicy(logger);
            var autostartService = new WindowsAutostartService(
                Path.Combine(_root, "BDO-UA-Client.exe"), logger);

            _form = new MainForm(
                applicationConfigStore,
                gameCatalog,
                _sessionHost,
                logger,
                appVersionInfo,
                githubClient,
                selectionPolicy,
                _appPaths,
                autostartService,
                startInBackground,
                _singleInstanceCoordinator);

            _form.HandleCreated += (_, _) => _formReady.TrySetResult(_form);
            if (exitWhenShown)
            {
                _form.Shown += (_, _) => _form.BeginInvoke(new Action(Application.ExitThread));
            }

            Application.Run(_form);
        }
        catch (Exception ex)
        {
            _hostException = ex;
            _formReady.TrySetException(ex);
        }
        finally
        {
            try
            {
                if (_form != null && !_form.IsDisposed)
                    _form.Dispose();
            }
            catch (Exception ex)
            {
                _hostException ??= ex;
            }

            _hostCompleted.TrySetResult(null);
        }
    }

    private async Task<T> WaitForAsync<T>(Func<MainForm, T?> read)
        where T : class
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Check()
        {
            try
            {
                if (_form == null || _form.IsDisposed || _form.Disposing)
                {
                    completion.TrySetException(new InvalidOperationException("MainForm closed before condition was met."));
                    return;
                }

                var result = read(_form);
                if (result != null)
                {
                    completion.TrySetResult(result);
                    return;
                }

                _form.BeginInvoke((Action)Check);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        PostToUi(Check);
        return await completion.Task.WaitAsync(Timeout);
    }

    private void PostToUi(Action action)
    {
        var form = _formReady.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
        form.BeginInvoke(action);
    }

    internal static string? FindControlText(Control root, Func<string, bool> predicate)
    {
        foreach (Control child in root.Controls)
        {
            if (predicate(child.Text))
                return child.Text;

            var nested = FindControlText(child, predicate);
            if (nested != null)
                return nested;
        }

        return null;
    }

    internal static LocalizationModeCard? FindFirstModeCard(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is LocalizationModeCard card)
                return card;

            var nested = FindFirstModeCard(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    internal static List<LocalizationModeCard> FindModeCards(Control root)
    {
        var cards = root.Controls.OfType<LocalizationModeCard>().ToList();
        foreach (Control child in root.Controls)
            cards.AddRange(FindModeCards(child));
        return cards;
    }

    internal static int CountModeCards(Control root)
    {
        var count = root.Controls.OfType<LocalizationModeCard>().Count();
        foreach (Control child in root.Controls)
            count += CountModeCards(child);
        return count;
    }

    internal sealed record StartupSnapshot(
        MainForm Form,
        string? GameStatus,
        string? GamePath,
        string? Message,
        int ApiRequestCount,
        int GitHubRequestCount);

    internal sealed class TestLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

internal sealed class MainFormTestHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _response;
    private readonly bool _waitForRelease;
    private readonly TaskCompletionSource<object?> _requestStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<object?> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _requestCount;

    internal MainFormTestHttpHandler(
        HttpStatusCode statusCode,
        string response,
        bool waitForRelease = false)
    {
        _statusCode = statusCode;
        _response = response;
        _waitForRelease = waitForRelease;
    }

    internal HttpStatusCode StatusCode => _statusCode;

    internal int RequestCount => Volatile.Read(ref _requestCount);

    internal Task RequestStarted => _requestStarted.Task;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal) == true)
        {
            Interlocked.Increment(ref _requestCount);
            _requestStarted.TrySetResult(null);

            if (_waitForRelease)
                await _release.Task.WaitAsync(cancellationToken);
        }

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_response, Encoding.UTF8, "application/json")
        };
    }

    internal void ReleasePendingRequest()
    {
        _release.TrySetResult(null);
    }
}
