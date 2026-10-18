using System.Net;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
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
    public async Task SwitchingBdoWwmBdo_UsesIsolatedSessionsAndWwmApiModes()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()));

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.GameSelector.Enabled);
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && form.GameSectionCaption == "Where Winds Meet — Winds4UA / W4U"
            && !form.IsSwitchInProgress
            && form.ActivePersistenceRoot == fixture.AppPaths.GetGamePersistencePaths("where-winds-meet").Root
            && FindWwmModeCards(form).Count == 1);
        AssertGameSwitchSettled(fixture.Form);
        Assert.IsType<WwmGameSession>(fixture.Form.ActiveSessionForTest);
        var cards = FindWwmModeCards(fixture.Form);
        Assert.Contains(cards, card => card.ModeSlug == "ukrainian" && card.ActionVisibleForTest && card.ActionEnabledForTest);
        Assert.DoesNotContain(cards, card => card.ModeSlug == "english-items");
        Assert.DoesNotContain(cards, card => card.ModeSlug == "english-terms");

        await fixture.SelectGameAsync("black-desert-online");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "black-desert-online"
            && form.GameSectionCaption == "Black Desert Online"
            && !form.IsSwitchInProgress
            && form.ActivePersistenceRoot == fixture.AppPaths.GetGamePersistencePaths("black-desert-online").Root);
        AssertGameSwitchSettled(fixture.Form);
        Assert.IsType<BdoGameSession>(fixture.Form.ActiveSessionForTest);
        Assert.Equal("Black Desert Online", fixture.Form.GameSelector.Text);

        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && !form.IsSwitchInProgress
            && FindWwmModeCards(form).Count == 1);
        AssertGameSwitchSettled(fixture.Form);
    }

    [Fact]
    public async Task WwmModes_HideUnavailableModes()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()));

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && FindWwmModeCards(form).Count == 1);

        Assert.DoesNotContain(FindWwmModeCards(fixture.Form), card => card.ModeSlug is "english-items" or "english-terms");
    }

    [Fact]
    public async Task WwmModeCardsShowCurrentApiReleaseVersionAndRefreshInPlace()
    {
        string? version = "v2.6.3";
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(
                CreateTinyWwmArchive(), currentVersion: () => version));

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.WwmReleaseVersionVisibleForTest("ukrainian", "default"));
        Assert.Equal("Версія релізу: v2.6.3", fixture.Form.WwmReleaseVersionForTest("ukrainian", "default"));

        version = "v2.6.4";
        var session = Assert.IsType<WwmGameSession>(fixture.Form.ActiveSessionForTest);
        await session.Poller.RequestImmediatePollAsync();
        await fixture.WaitForAsync(form => form.WwmReleaseVersionForTest("ukrainian", "default") == "Версія релізу: v2.6.4");

        version = null;
        await session.Poller.RequestImmediatePollAsync();
        await fixture.WaitForAsync(form => !form.WwmReleaseVersionVisibleForTest("ukrainian", "default"));
        Assert.Empty(fixture.Form.WwmReleaseVersionForTest("ukrainian", "default") ?? string.Empty);

        version = "v2.6.4";
        await session.Poller.RequestImmediatePollAsync();
        await fixture.WaitForAsync(form => form.WwmReleaseVersionForTest("ukrainian", "default") == "Версія релізу: v2.6.4");
        await fixture.RunWwmInstallAsync("ukrainian", "default", confirm: true);
        Assert.True(fixture.Form.WwmModeInstalledForTest("ukrainian", "default"));
        Assert.Equal("Версія релізу: v2.6.4", fixture.Form.WwmReleaseVersionForTest("ukrainian", "default"));
    }

    [Fact]
    public async Task WwmRecognizedUnknownWindowsRoot_IsNotPresentedAsSteamAndUsesUnambiguousLayout()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()),
            wwmHasSteamManifest: false,
            wwmUsesLocalDataPatchLayout: true);

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && !form.IsSwitchInProgress
            && FindWwmModeCards(form).Count == 1
            && form.WwmGameStatusForTest == "Знайдено Where Winds Meet");

        var card = Assert.Single(FindWwmModeCards(fixture.Form));
        Assert.DoesNotContain("Steam", fixture.Form.WwmGameStatusForTest, StringComparison.OrdinalIgnoreCase);
        var session = Assert.IsType<WwmGameSession>(fixture.Form.ActiveSessionForTest);
        Assert.Equal(WwmInstallationSource.UnknownWindows, session.CurrentDetection.Source);
        Assert.True(session.MutationSupported);
        Assert.True(card.ActionVisibleForTest);
        Assert.True(card.ActionEnabledForTest);
        Assert.False(fixture.Form.WwmRestoreEnabledForTest);
        Assert.True(fixture.Form.WwmInstallActionsEnabledForTest);
        Assert.Equal(Path.GetFullPath(fixture.WwmGameRoot), fixture.Form.WwmGamePathForTest);
    }

    [Fact]
    public async Task WwmProvenSteamRoot_PresentsSteamBuildAndKeepsMutationEnabled()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()));

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && form.WwmGameStatusForTest.Contains("Знайдено Steam-гру · build 25281753", StringComparison.Ordinal)
            && FindWwmModeCards(form).Count == 1);

        var card = Assert.Single(FindWwmModeCards(fixture.Form));
        Assert.True(card.ActionVisibleForTest);
        Assert.True(card.ActionEnabledForTest);
        Assert.DoesNotContain("сумісність не підтверджено", fixture.Form.WwmGameStatusForTest, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(null, true)]
    public async Task WwmInstall_CompatibilityConfirmationFollowsFreshApiTestStatus(bool? gameTested, bool expectedPrompt)
    {
        var archive = CreateTinyWwmArchive();
        var requests = new List<string>();
        var promptCount = 0;
        string? promptTitle = null;
        string? promptBody = null;
        bool? apiGameTested = gameTested == null ? false : !gameTested.Value;
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(
                archive, requests, gameTested: () => apiGameTested));

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && FindWwmModeCards(form).Any(card => card.ModeSlug == "ukrainian" && card.ActionEnabledForTest));

        // The install-time latest response deliberately differs from the feed loaded for presentation.
        apiGameTested = gameTested;
        await fixture.RunWwmInstallAsync("ukrainian", "default", confirm: true,
            confirmationObserved: (title, body) =>
            {
                promptCount++;
                promptTitle = title;
                promptBody = body;
            });

        Assert.Equal(expectedPrompt ? 1 : 0, promptCount);
        Assert.Contains(requests, path => path.EndsWith(".zip", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(fixture.AppPaths.GetGamePersistencePaths("where-winds-meet").StateDir, "wwm-installation.json")));
        if (expectedPrompt)
        {
            Assert.Equal("Сумісність релізу з поточною версією гри не підтверджена", promptTitle);
            Assert.Contains("Winds4UA API", promptBody, StringComparison.Ordinal);
            Assert.DoesNotContain("Steam build", promptBody, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task WwmNonSteamInstall_UsesSameApiCompatibilityPrompt(bool epic, bool gameTested)
    {
        var promptCount = 0;
        string? promptBody = null;
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(
                CreateTinyWwmArchive(), gameTested: () => gameTested),
            wwmHasSteamManifest: false,
            wwmHasEpicManifest: epic);

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && form.WwmInstallActionsEnabledForTest);

        await fixture.RunWwmInstallAsync("ukrainian", "default", confirm: true,
            confirmationObserved: (_, body) =>
            {
                promptCount++;
                promptBody = body;
            });

        Assert.Equal(gameTested ? 0 : 1, promptCount);
        Assert.DoesNotContain("Steam", fixture.Form.WwmGameStatusForTest, StringComparison.OrdinalIgnoreCase);
        if (!gameTested)
        {
            Assert.Contains("Winds4UA API", promptBody, StringComparison.Ordinal);
            Assert.DoesNotContain("Steam build", promptBody, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task WwmInstall_UntestedDeclineCancelsBeforeDownloadOrMutation()
    {
        var archive = CreateTinyWwmArchive();
        var requests = new List<string>();
        var promptCount = 0;
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(
                archive, requests, gameTested: () => false));

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && FindWwmModeCards(form).Any(card => card.ModeSlug == "ukrainian" && card.ActionEnabledForTest));
        var managedPath = Path.Combine(fixture.WwmGameRoot, WwmGameDefinition.Default.ManagedRelativePaths[0]);
        var original = new byte[] { 71, 72, 73 };
        Directory.CreateDirectory(Path.GetDirectoryName(managedPath)!);
        await File.WriteAllBytesAsync(managedPath, original);

        await fixture.RunWwmInstallAsync("ukrainian", "default", confirm: false,
            confirmationObserved: (_, _) => promptCount++);

        Assert.Equal(1, promptCount);
        Assert.Equal(original, await File.ReadAllBytesAsync(managedPath));
        Assert.DoesNotContain(requests, path => path.EndsWith(".zip", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(fixture.AppPaths.GetGamePersistencePaths("where-winds-meet").StateDir, "wwm-installation.json")));
        Assert.Equal(OperationState.Cancelled, fixture.Form.OperationStateForTest);
    }

    [Fact]
    public async Task ExternalGameUpdate_RemainsInstallableThroughRealMainFormAndInstallServiceFlow()
    {
        var archive = CreateTinyWwmArchive();
        var previousInstalledBytes = new[] { new byte[] { 10, 11, 12 }, new byte[] { 13, 14, 15, 16 } };
        var externalCurrentBytes = new[] { new byte[] { 21, 22, 23 }, new byte[] { 31, 32, 33, 34 } };
        var preHubBytes = new[] { new byte[] { 41, 42 }, new byte[] { 51, 52, 53 } };
        var cycle = Guid.NewGuid().ToString("N");
        var archiveSha = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(archive, gameTested: () => true),
            wwmSteamBuildId: "25906190",
            beforeUiThreadStart: async fixture =>
            {
                var definition = WwmGameDefinition.Default;
                var paths = fixture.AppPaths.GetGamePersistencePaths(definition.Id);
                var store = new WwmStateStore(paths);
                var targets = definition.GetManagedRelativePaths(WwmInstallationLayout.Package);
                var binding = new WwmInstallationBinding(
                    WwmGameDefinition.NormalizeInstallationRoot(fixture.WwmGameRoot),
                    WwmInstallationSource.Steam,
                    WwmInstallationLayout.Package,
                    "25906190",
                    WwmGameDefinition.ComputeInstallationFingerprint(fixture.WwmGameRoot));

                for (var index = 0; index < targets.Length; index++)
                {
                    var target = Path.Combine(fixture.WwmGameRoot, targets[index]);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await File.WriteAllBytesAsync(target, externalCurrentBytes[index]);
                }

                await store.SaveAsync(new WwmInstallationState
                {
                    SchemaVersion = 2,
                    ModeSlug = "ukrainian",
                    ModeVariant = "default",
                    ReleaseVersion = "v2.6.3",
                    ArchiveSizeBytes = 1,
                    ArchiveSha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1 })).ToLowerInvariant(),
                    PreHubSnapshotCycleId = cycle,
                    GameBuildId = "25281753",
                    InstallationRoot = binding.GameRoot,
                    TargetLayout = WwmInstallationLayout.Package.ToString(),
                    InstallationFingerprint = binding.Fingerprint,
                    InstalledAt = DateTimeOffset.UtcNow,
                    Targets = previousInstalledBytes.Select((bytes, index) => new WwmTargetManifestEntry
                    {
                        RelativePath = targets[index], SizeBytes = bytes.Length,
                        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                    }).ToList()
                });

                Directory.CreateDirectory(store.SnapshotDirectory);
                var snapshotTargets = new List<WwmPreviousTarget>();
                for (var index = 0; index < targets.Length; index++)
                {
                    var backupName = $"{index}.bin";
                    await File.WriteAllBytesAsync(Path.Combine(store.SnapshotDirectory, backupName), preHubBytes[index]);
                    snapshotTargets.Add(new WwmPreviousTarget
                    {
                        RelativePath = targets[index], Existed = true, SizeBytes = preHubBytes[index].Length,
                        Sha256 = Convert.ToHexString(SHA256.HashData(preHubBytes[index])).ToLowerInvariant(),
                        BackupFile = backupName, Attributes = (int)FileAttributes.Archive
                    });
                }
                await File.WriteAllTextAsync(Path.Combine(store.SnapshotDirectory, "snapshot.json"), JsonSerializer.Serialize(
                    new WwmPreHubSnapshot
                    {
                        SchemaVersion = 2,
                        CycleOperationId = cycle,
                        GameBuildId = "25281753",
                        InstallationRoot = binding.GameRoot,
                        TargetLayout = WwmInstallationLayout.Package.ToString(),
                        InstallationFingerprint = binding.Fingerprint,
                        Targets = snapshotTargets
                    }));
            });

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && FindWwmModeCards(form).Any(card => card.ModeSlug == "ukrainian"));

        var session = Assert.IsType<WwmGameSession>(fixture.Form.ActiveSessionForTest);
        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package);
        var stateStore = session.StateStore;

        // Owner-shaped state and external target changes predate the real WWM startup lifecycle.
        Assert.False(fixture.Form.WwmRecoveryBlockedForTest);
        var beforeInstallCard = Assert.Single(FindWwmModeCards(fixture.Form));
        Assert.Equal("Файли гри змінено — потрібне повторне встановлення", beforeInstallCard.StateTextForTest);
        Assert.Equal("Встановити", beforeInstallCard.ActionTextForTest);
        Assert.True(beforeInstallCard.ActionVisibleForTest);
        Assert.True(beforeInstallCard.ActionEnabledForTest);
        Assert.False(fixture.Form.WwmRestoreEnabledForTest);
        for (var index = 0; index < targets.Length; index++)
            Assert.Equal(externalCurrentBytes[index], await File.ReadAllBytesAsync(Path.Combine(fixture.WwmGameRoot, targets[index])));

        await fixture.RunWwmInstallAsync("ukrainian", "default", confirm: true);

        Assert.True(fixture.Form.OperationStateForTest == OperationState.Completed,
            $"Expected completed install; state={fixture.Form.OperationStateForTest}, message={fixture.Form.OperationMessageForTest}, logs={string.Join(" | ", fixture.Logger.Warnings)}");
        Assert.False(fixture.Form.WwmRecoveryBlockedForTest);
        Assert.Equal(OperationState.Completed, fixture.Form.OperationStateForTest);
        for (var index = 0; index < targets.Length; index++)
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(fixture.WwmGameRoot, targets[index])));
        var installedState = stateStore.Load(out var stateError);
        Assert.Null(stateError);
        Assert.NotNull(installedState);
        Assert.Equal("ukrainian", installedState!.ModeSlug);
        Assert.Equal("default", installedState.ModeVariant);
        Assert.Equal("v-test", installedState.ReleaseVersion);
        Assert.Equal(archiveSha, installedState.ArchiveSha256);
        Assert.Null(stateStore.LoadJournal(out _));
        var installedCard = Assert.Single(FindWwmModeCards(fixture.Form));
        Assert.Equal("✓ Встановлено", installedCard.StateTextForTest);
        Assert.True(installedCard.InstalledBadgeVisibleForTest);
        Assert.Contains("успішно встановлено", fixture.Form.OperationMessageForTest, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WwmInstalledStateKind.Current,
            (await session.InstallService.ResolveStateAsync(session.CurrentDetection,
                new WwmApiPackage { Mode = new WwmMode { Slug = "ukrainian", Variant = "default" }, Version = "v-test",
                    DownloadUrl = "https://winds4ua.com.ua/releases/test.zip", SizeBytes = archive.Length, Sha256 = archiveSha })).State);
        var restore = await session.InstallService.CheckRestoreAvailabilityAsync(session.CurrentDetection);
        Assert.False(restore.IsAvailable);
        Assert.Contains("Steam build", restore.Message, StringComparison.Ordinal);
        Assert.Equal("25281753", JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(stateStore.SnapshotDirectory, "snapshot.json")))
            .RootElement.GetProperty("game_build_id").GetString());
        for (var index = 0; index < targets.Length; index++)
            Assert.Equal(preHubBytes[index], await File.ReadAllBytesAsync(Path.Combine(stateStore.SnapshotDirectory, $"{index}.bin")));

        await fixture.SelectGameAsync("black-desert-online");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && FindWwmModeCards(form).Any(card => card.ModeSlug == "ukrainian" && card.InstalledBadgeVisibleForTest));
        var restartedCard = Assert.Single(FindWwmModeCards(fixture.Form));
        Assert.False(fixture.Form.WwmRecoveryBlockedForTest);
        Assert.Equal("✓ Встановлено", restartedCard.StateTextForTest);
        Assert.DoesNotContain("повторне встановлення", restartedCard.StateTextForTest, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OperationState.Idle, fixture.Form.OperationStateForTest);
        Assert.Null(stateStore.LoadJournal(out _));
        for (var index = 0; index < targets.Length; index++)
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(fixture.WwmGameRoot, targets[index])));
    }

    [Fact]
    public async Task WwmEpicRoot_IsPresentedAsEpicOnlyWithMatchingLauncherMetadata()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()),
            wwmHasSteamManifest: false,
            wwmHasEpicManifest: true);

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && form.WwmGameStatusForTest == "Знайдено Epic Games-версію"
            && form.WwmInstallActionsEnabledForTest);

        var session = Assert.IsType<WwmGameSession>(fixture.Form.ActiveSessionForTest);
        Assert.Equal(WwmInstallationSource.EpicGames, session.CurrentDetection.Source);
        Assert.Equal(WwmInstallationLayout.Package, session.CurrentDetection.TargetLayout);
        Assert.True(fixture.Form.WwmInstallActionsEnabledForTest);
    }

    [Fact]
    public async Task WwmUnknownDualLayout_IsPresentedButAllMutationActionsStayDisabled()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()),
            wwmHasSteamManifest: false,
            wwmAlsoHasLocalDataPatchLayout: true);

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && form.WwmGameStatusForTest == "Знайдено Where Winds Meet"
            && form.OperationMessageForTest.Contains("кілька цільових розкладок", StringComparison.Ordinal));

        var session = Assert.IsType<WwmGameSession>(fixture.Form.ActiveSessionForTest);
        Assert.Equal(WwmInstallationSource.UnknownWindows, session.CurrentDetection.Source);
        Assert.True(session.CurrentDetection.HasAmbiguousTargetLayout);
        Assert.False(session.MutationSupported);
        Assert.False(fixture.Form.WwmInstallActionsEnabledForTest);
        Assert.False(fixture.Form.WwmRestoreEnabledForTest);
    }

    [Fact]
    public async Task WwmManualDetection_BlocksConflictingActionsAndRestoresSteamMutationAfterCompletion()
    {
        var traversalEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueTraversal = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEnumeration = 0;
        IEnumerable<string> EnumerateWithPause(string path)
        {
            if (Interlocked.Exchange(ref firstEnumeration, 1) == 0)
            {
                traversalEntered.TrySetResult(null);
                continueTraversal.Task.GetAwaiter().GetResult();
            }
            return Directory.EnumerateDirectories(path).ToArray();
        }

        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            includeSyntheticSecondGame: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()),
            wwmManualDirectoryEnumerator: EnumerateWithPause);
        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && form.WwmInstallActionsEnabledForTest);

        var session = Assert.IsType<WwmGameSession>(fixture.Form.ActiveSessionForTest);
        var detectionBeforeScan = session.CurrentDetection;
        var installCalls = 0;
        var scanTask = await fixture.BeginWwmManualPathSearchAsync(fixture.WwmSelectedParentForTest);
        await traversalEntered.Task.WaitAsync(MainFormTestFixture.Timeout);

        Assert.True(fixture.Form.WwmManualDetectionInProgressForTest);
        Assert.False(fixture.Form.GameSelector.Enabled);
        Assert.False(fixture.Form.WwmDetectEnabledForTest);
        Assert.False(fixture.Form.WwmInstallActionsEnabledForTest);
        Assert.False(fixture.Form.WwmRestoreEnabledForTest);

        var duplicateScan = await fixture.BeginWwmManualPathSearchAsync(fixture.WwmSelectedParentForTest);
        Assert.Null(await duplicateScan.WaitAsync(MainFormTestFixture.Timeout));
        Assert.True(fixture.Form.WwmManualDetectionInProgressForTest);

        await fixture.InvokeWwmInstallAsync("ukrainian", "default", _ =>
        {
            Interlocked.Increment(ref installCalls);
            return Task.FromResult(WwmMutationResult.Success());
        });
        var restoreResult = await fixture.InvokeWwmRestoreAsync();
        await fixture.InvokeDetectGameAsync();
        var selectedWhileBusy = await fixture.SelectGameAsync("synthetic-game");

        Assert.Equal(0, installCalls);
        Assert.Equal(WwmMutationError.InvalidRoot, restoreResult.Error);
        Assert.Same(detectionBeforeScan, fixture.Form.WwmCurrentDetectionForTest);
        Assert.Equal("where-winds-meet", fixture.Form.SelectedGame.Id);
        Assert.Equal("where-winds-meet", selectedWhileBusy);
        Assert.False(fixture.Form.IsSwitchInProgress);
        Assert.True(fixture.Form.WwmManualDetectionInProgressForTest);

        continueTraversal.TrySetResult(null);
        var result = await scanTask.WaitAsync(MainFormTestFixture.Timeout);

        Assert.NotNull(result);
        Assert.Equal(ManualResolveStatus.Found, result!.Status);
        await fixture.WaitForAsync(form => !form.WwmManualDetectionInProgressForTest
            && form.WwmInstallActionsEnabledForTest
            && form.WwmDetectEnabledForTest
            && form.GameSelector.Enabled);
        Assert.True(session.MutationSupported);
        Assert.Equal(WwmInstallationSource.Steam, session.CurrentDetection.Source);
        Assert.Equal(Path.GetFullPath(fixture.WwmGameRoot), session.GameRoot);
        Assert.Equal(Path.GetFullPath(fixture.WwmGameRoot), fixture.Form.WwmGamePathForTest);
    }

    [Fact]
    public async Task WwmManualDetection_CancellationDoesNotPersistOrApplyStaleResult()
    {
        var traversalEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueTraversal = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEnumeration = 0;
        IEnumerable<string> EnumerateWithPause(string path)
        {
            if (Interlocked.Exchange(ref firstEnumeration, 1) == 0)
            {
                traversalEntered.TrySetResult(null);
                continueTraversal.Task.GetAwaiter().GetResult();
            }
            return Directory.EnumerateDirectories(path).ToArray();
        }

        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()),
            wwmManualDirectoryEnumerator: EnumerateWithPause);
        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && form.WwmManualDetectionInProgressForTest == false);

        var session = Assert.IsType<WwmGameSession>(fixture.Form.ActiveSessionForTest);
        var originalDetection = session.CurrentDetection;
        var originalRoot = session.GameRoot;
        var originalConfigPath = session.ConfigStore.Load().Value?.GamePath;
        using var cancellation = new CancellationTokenSource();
        var scanTask = await fixture.BeginWwmManualPathSearchAsync(fixture.WwmSelectedParentForTest, cancellation.Token);
        await traversalEntered.Task.WaitAsync(MainFormTestFixture.Timeout);

        cancellation.Cancel();
        continueTraversal.TrySetResult(null);
        var result = await scanTask.WaitAsync(MainFormTestFixture.Timeout);

        Assert.Null(result);
        Assert.Same(originalDetection, session.CurrentDetection);
        Assert.Equal(originalRoot, session.GameRoot);
        Assert.Equal(originalConfigPath, session.ConfigStore.Load().Value?.GamePath);
        await fixture.WaitForAsync(form => !form.WwmManualDetectionInProgressForTest
            && form.WwmDetectEnabledForTest
            && form.GameSelector.Enabled);
    }

    [Fact]
    public void WwmIncompleteManualSearch_HasTruthfulNonNotFoundMessage()
    {
        var message = MainForm.GetWwmManualResolutionMessage(ManualResolveStatus.Incomplete);

        Assert.Contains("Пошук не завершено", message, StringComparison.Ordinal);
        Assert.Contains("Оберіть папку ближче", message, StringComparison.Ordinal);
        Assert.DoesNotContain("не знайдено", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("кілька встановлень", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WwmNotFoundPresentation_DoesNotClaimSteamWasNotFound()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()),
            wwmGameMarkersPresent: false);

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && !form.IsSwitchInProgress
            && form.WwmGameStatusForTest == "Гру не знайдено");

        Assert.DoesNotContain("Steam", fixture.Form.WwmGameStatusForTest, StringComparison.OrdinalIgnoreCase);
        Assert.False(fixture.Form.WwmRestoreEnabledForTest);
    }

    [Fact]
    public async Task WwmSavedUnavailableMode_FallsBackToFirstVisibleInstallableMode()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()),
            wwmConfigJson: "{\"last_mode\":\"english-items\",\"last_mode_variant\":\"english_items\"}");

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && FindWwmModeCards(form).Count == 1);

        var visibleCard = Assert.Single(FindWwmModeCards(fixture.Form));
        Assert.Equal("ukrainian", visibleCard.ModeSlug);
        Assert.True(visibleCard.IsSelected);
    }

    [Fact]
    public async Task WwmAvailableModeWithoutMatchingArtifact_IsNotRendered()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(
                CreateTinyWwmArchive(), omitCurrentArtifact: true));

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && FindWwmModeCards(form).Count == 0
            && form.OperationStateForTest == OperationState.Idle);

        Assert.Contains("Наразі немає доступних режимів", fixture.Form.ModePlaceholderTextForTest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulGameSwitch_DoesNotClearWwmApiFailureMessage()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(
                CreateTinyWwmArchive(), latestStatus: HttpStatusCode.BadGateway));

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && !form.IsSwitchInProgress
            && form.OperationStateForTest == OperationState.Idle
            && !string.IsNullOrWhiteSpace(form.OperationMessageForTest));

        Assert.DoesNotContain("Завантаження даних для обраної гри...", fixture.Form.OperationMessageForTest, StringComparison.Ordinal);
    }

    private static void AssertGameSwitchSettled(MainForm form)
    {
        Assert.Equal(OperationState.Idle, form.OperationStateForTest);
        Assert.DoesNotContain("Завантаження даних для обраної гри...", form.OperationMessageForTest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WwmInstall_DeclineDoesNotDownloadAndAcceptUsesFreshPackage()
    {
        var archive = CreateTinyWwmArchive();
        var wwmRequests = new List<string>();
        var handlerFactory = MainFormTestFixture.CreateWwmApiHandlerFactory(archive, wwmRequests, gameTested: () => false);
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: handlerFactory);
        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.GameSelector.Enabled);
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && FindWwmModeCards(form).Count == 1);

        var fakeRoot = fixture.WwmGameRoot;
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var original = new byte[] { 31, 32 };
        await File.WriteAllBytesAsync(Path.Combine(fakeRoot, targets[0]), original);
        await fixture.RunWwmInstallAsync("ukrainian", "default", confirm: false);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(fakeRoot, targets[0])));
        Assert.False(File.Exists(Path.Combine(fixture.AppPaths.GetGamePersistencePaths("where-winds-meet").StateDir, "wwm-installation.json")));
        Assert.DoesNotContain(wwmRequests, path => path.EndsWith(".zip", StringComparison.Ordinal));

        await fixture.RunWwmInstallAsync("ukrainian", "default", confirm: true);
        Assert.True(File.Exists(Path.Combine(fakeRoot, targets[0])));
        Assert.True(File.Exists(Path.Combine(fakeRoot, targets[1])), fixture.Form.OperationMessageForTest + " | " + string.Join(" || ", fixture.Logger.Warnings));
        Assert.True(File.Exists(Path.Combine(fixture.AppPaths.GetGamePersistencePaths("where-winds-meet").StateDir, "wwm-installation.json")), fixture.Form.OperationMessageForTest);
        Assert.Contains(wwmRequests, path => path.EndsWith(".zip", StringComparison.Ordinal));
        var installedCard = Assert.Single(FindWwmModeCards(fixture.Form));
        Assert.Equal("✓ Встановлено", installedCard.StateTextForTest);
        Assert.True(installedCard.InstalledBadgeVisibleForTest);
        Assert.Equal("✓ Встановлено", installedCard.InstalledBadgeTextForTest);
        Assert.Equal(UiTheme.Success, installedCard.InstalledBadgeForeColorForTest);
        Assert.Equal(UiTheme.SuccessSurface, installedCard.InstalledBadgeBackColorForTest);
        Assert.True(installedCard.IsInstalledForTest);
        Assert.False(installedCard.StatusLabelVisibleForTest);
        Assert.False(installedCard.ActionVisibleForTest);
        Assert.False(installedCard.ActionEnabledForTest);
    }

    [Fact]
    public async Task WwmInstall_FreshResponseUnavailableBlocksPreviouslyVisibleMode()
    {
        var available = true;
        var requests = new List<string>();
        var factory = MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive(), requests, () => available);
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(), includeWwm: true, wwmHandlerFactory: factory);
        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && FindWwmModeCards(form).Any(card => card.ModeSlug == "ukrainian" && card.ActionEnabledForTest));

        available = false;
        await fixture.RunWwmInstallAsync("ukrainian", "default", confirm: true);

        Assert.DoesNotContain(requests, path => path.EndsWith(".zip", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(fixture.WwmGameRoot, WwmGameDefinition.Default.ManagedRelativePaths[0])));
        Assert.False(File.Exists(Path.Combine(fixture.AppPaths.GetGamePersistencePaths("where-winds-meet").StateDir, "wwm-installation.json")));
    }

    [Theory]
    [InlineData(WwmMutationError.RollbackFailed)]
    [InlineData(WwmMutationError.RecoveryRequired)]
    [InlineData(WwmMutationError.SnapshotStale)]
    public async Task WwmInstall_UnresolvedRecoveryResultImmediatelyBlocksCurrentSession(WwmMutationError error)
    {
        const string criticalMessage = "Критичний стан транзакції WWM потребує відновлення.";
        var requests = new List<string>();
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive(), requests));

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && FindWwmModeCards(form).Any(card => card.ModeSlug == "ukrainian" && card.ActionEnabledForTest));

        await fixture.RunWwmInstallAsync("ukrainian", "default", confirm: true,
            result: WwmMutationResult.Failure(error, criticalMessage));

        Assert.True(fixture.Form.WwmRecoveryBlockedForTest);
        Assert.False(fixture.Form.WwmRestoreEnabledForTest);
        Assert.Equal(criticalMessage, fixture.Form.OperationMessageForTest);
        var card = Assert.Single(FindWwmModeCards(fixture.Form));
        Assert.Equal("Потрібне відновлення", card.StateTextForTest);
        Assert.False(card.ActionVisibleForTest);
        Assert.False(card.ActionEnabledForTest);
        Assert.DoesNotContain(requests, path => path.EndsWith(".zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WwmInstall_RolledBackMutationFailureDoesNotBlockSession()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()));

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && FindWwmModeCards(form).Any(card => card.ModeSlug == "ukrainian" && card.ActionEnabledForTest));

        await fixture.RunWwmInstallAsync("ukrainian", "default", confirm: true,
            result: WwmMutationResult.Failure(WwmMutationError.Mutation, "Зміни безпечно відкотилися."));

        Assert.False(fixture.Form.WwmRecoveryBlockedForTest);
        Assert.False(fixture.Form.WwmRestoreEnabledForTest);
        var card = Assert.Single(FindWwmModeCards(fixture.Form));
        Assert.Equal("Встановити", card.ActionTextForTest);
        Assert.True(card.ActionEnabledForTest);
    }

    [Fact]
    public async Task Startup_PersistedWwmSelectionActivatesWwmSession()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            applicationConfigJson: "{\"selected_game_id\":\"where-winds-meet\"}",
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()));

        await fixture.Form.WaitForStartupCompletionForTestAsync().WaitAsync(MainFormTestFixture.Timeout);
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && form.GameSectionCaption == "Where Winds Meet — Winds4UA / W4U"
            && FindWwmModeCards(form).Count == 1);

        Assert.IsType<WwmGameSession>(fixture.Form.ActiveSessionForTest);
        var selected = new ApplicationConfigStore(fixture.AppPaths, new MainFormTestFixture.TestLogger()).Load();
        Assert.Equal("where-winds-meet", selected.Value!.SelectedGameId);
    }

    [Fact]
    public async Task Startup_MalformedWwmJournalBlocksMutationPresentationAndStillAllowsSwitchingAway()
    {
        var requests = new List<string>();
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive(), requests),
            seedMalformedWwmJournal: true);
        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "where-winds-meet"
            && form.WwmRecoveryBlockedForTest
            && FindWwmModeCards(form).Count == 1);

        Assert.Equal(OperationState.Idle, fixture.Form.OperationStateForTest);
        Assert.Contains("Журнал відновлення WWM пошкоджено", fixture.Form.OperationMessageForTest, StringComparison.Ordinal);
        Assert.False(fixture.Form.WwmRestoreEnabledForTest);
        Assert.All(FindWwmModeCards(fixture.Form), card =>
        {
            Assert.Equal("Потрібне відновлення", card.StateTextForTest);
            Assert.False(card.ActionEnabledForTest);
            Assert.False(card.ActionVisibleForTest);
        });
        await fixture.RunWwmInstallAsync("ukrainian", "default", confirm: true);
        Assert.DoesNotContain(requests, path => path.EndsWith(".zip", StringComparison.Ordinal));
        Assert.All(WwmGameDefinition.Default.ManagedRelativePaths, path =>
            Assert.False(File.Exists(Path.Combine(fixture.WwmGameRoot, path))));

        await fixture.SelectGameAsync("black-desert-online");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.SelectedGame.Id == "black-desert-online" && !form.IsSwitchInProgress);
    }

    [Fact]
    public async Task WwmManualTrustedRoot_RetriesBlockedLegacyRecoveryAndRefreshesWithoutRestart()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()),
            wwmInvalidConfigInitially: true,
            seedLegacyWwmState: true,
            wwmSteamBuildId: "25906190");

        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.WwmRecoveryBlockedForTest
            && FindWwmModeCards(form).Count == 1);

        Assert.Contains("Налаштування шляху Where Winds Meet неможливо безпечно оновити", fixture.Form.OperationMessageForTest, StringComparison.Ordinal);

        var session = Assert.IsType<WwmGameSession>(fixture.Form.ActiveSessionForTest);
        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package);
        var before = await Task.WhenAll(targets.Select(path => File.ReadAllBytesAsync(Path.Combine(fixture.WwmGameRoot, path))));
        var selected = await fixture.BeginWwmManualPathSearchAsync(fixture.WwmGameRoot);
        var result = await selected.WaitAsync(MainFormTestFixture.Timeout);

        Assert.NotNull(result);
        Assert.Equal(ManualResolveStatus.Found, result!.Status);
        Assert.False(fixture.Form.WwmRecoveryBlockedForTest);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.WwmGameRoot),
            session.CurrentDetection.PersistedGamePath);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.WwmGameRoot),
            session.ConfigStore.Load().Value!.GamePath);
        Assert.Equal(2, session.StateStore.Load(out _)!.SchemaVersion);
        await fixture.WaitForAsync(form => FindWwmModeCards(form).Any(card => card.ActionEnabledForTest));
        var installedCard = Assert.Single(FindWwmModeCards(fixture.Form));
        Assert.NotEqual("Потрібне відновлення", installedCard.StateTextForTest);
        Assert.True(installedCard.ActionTextForTest is "Встановити" or "Оновити");
        Assert.False(fixture.Form.WwmRestoreEnabledForTest); // The synthetic snapshot is stale by design.
        for (var index = 0; index < targets.Length; index++)
            Assert.Equal(before[index], await File.ReadAllBytesAsync(Path.Combine(fixture.WwmGameRoot, targets[index])));
    }

    [Fact]
    public async Task WwmManualTrustedRoot_RecoveryRetryFailureKeepsBlockedPresentation()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            includeWwm: true,
            wwmHandlerFactory: MainFormTestFixture.CreateWwmApiHandlerFactory(CreateTinyWwmArchive()),
            wwmInvalidConfigInitially: true,
            seedLegacyWwmState: true,
            wwmSteamBuildId: "25906190");
        await fixture.WaitForStartupAsync();
        await fixture.SelectGameAsync("where-winds-meet");
        await fixture.WaitForSwitchCompletionAsync();
        await fixture.WaitForAsync(form => form.WwmRecoveryBlockedForTest);

        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package);
        var before = await Task.WhenAll(targets.Select(path => File.ReadAllBytesAsync(Path.Combine(fixture.WwmGameRoot, path))));
        var snapshotPath = Path.Combine(fixture.AppPaths.GetGamePersistencePaths("where-winds-meet").BackupsDir,
            "pre-hub", "snapshot.json");
        await File.WriteAllTextAsync(snapshotPath, "{ malformed snapshot");
        var resultTask = await fixture.BeginWwmManualPathSearchAsync(fixture.WwmGameRoot);
        var result = await resultTask.WaitAsync(MainFormTestFixture.Timeout);

        Assert.Equal(ManualResolveStatus.Found, result!.Status);
        Assert.True(fixture.Form.WwmRecoveryBlockedForTest);
        Assert.Contains("Знімок WWM неможливо безпечно прочитати", fixture.Form.OperationMessageForTest, StringComparison.Ordinal);
        Assert.False(fixture.Form.WwmRestoreEnabledForTest);
        Assert.All(FindWwmModeCards(fixture.Form), card =>
        {
            Assert.Equal("Потрібне відновлення", card.StateTextForTest);
            Assert.False(card.ActionVisibleForTest);
            Assert.False(card.ActionEnabledForTest);
        });
        Assert.Equal(1, Assert.IsType<WwmGameSession>(fixture.Form.ActiveSessionForTest).StateStore.Load(out _)!.SchemaVersion);
        for (var index = 0; index < targets.Length; index++)
            Assert.Equal(before[index], await File.ReadAllBytesAsync(Path.Combine(fixture.WwmGameRoot, targets[index])));
    }

    private static List<WwmModeCard> FindWwmModeCards(Control root)
    {
        var cards = root.Controls.OfType<WwmModeCard>().ToList();
        foreach (Control child in root.Controls) cards.AddRange(FindWwmModeCards(child));
        return cards;
    }

    private static byte[] CreateTinyWwmArchive()
    {
        using var memory = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var path in WwmGameDefinition.Default.ManagedRelativePaths)
            {
                var entry = zip.CreateEntry(path.Replace('\\', '/'));
                using var output = entry.Open();
                output.Write(new byte[] { 1, 2, 3 });
            }
        }
        return memory.ToArray();
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
        Assert.Equal("Доступно", fixture.Form.WwmTargetStatus);
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
        var payload = Encoding.UTF8.GetBytes("patch 401 localization");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01AGGREGATEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([JsonResponse(HttpStatusCode.OK, CreateLatestJson("01LATEST401", payload, "verified", patch: 401), "\"latest-401\"")]),
            payload);
        using var fixture = await MainFormTestFixture.StartAsync(handler, gamePatch: 402);
        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.SelectedModeCardActionTextForTest == "Встановити");

        var gameFile = BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot);
        var originalBytes = await File.ReadAllBytesAsync(gameFile);
        await fixture.InstallForTestAsync();

        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(gameFile));
        Assert.DoesNotContain(handler.RequestPaths, path => path.StartsWith("/download/", StringComparison.Ordinal));
        Assert.Equal(GamePatchStatus.NewerThanLatestLocalization, fixture.Form.GamePatchStatusForTest);
        Assert.False(fixture.Form.SelectedModeCardActionEnabledForTest);
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

    [Fact]
    public async Task Install_UsesFreshLatestReleaseAsTransactionTarget()
    {
        var previouslyInstalledBytes = Encoding.UTF8.GetBytes("installed aggregate release A");
        var installedBytes = Encoding.UTF8.GetBytes("fresh release B");
        var latestJson = CreateLatestJson("01FRESHB", installedBytes, "verified");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01STALEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([JsonResponse(HttpStatusCode.OK, latestJson, "\"latest-b\""),
                JsonResponse(HttpStatusCode.OK, latestJson, "\"latest-b\"")]),
            installedBytes);
        using var fixture = await MainFormTestFixture.StartAsync(handler, gamePatch: 395);
        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => MainFormTestFixture.CountModeCards(form) == 1);
        var gameFile = BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot);
        var backupStore = new BackupStore(
            fixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id),
            new MainFormTestFixture.TestLogger());
        var originalSnapshot = await backupStore.CreateOriginalSnapshotAsync(fixture.GameRoot, trustedGamePatch: 395);
        Assert.True(originalSnapshot.IsSuccess);
        await File.WriteAllBytesAsync(gameFile, previouslyInstalledBytes);
        await new InstallationStateStore(
                fixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id),
                new MainFormTestFixture.TestLogger())
            .SaveAsync(new InstallationMetadata
            {
                ModeSlug = "english-items",
                PublicId = "01STALEA",
                Version = 1,
                GamePatch = 395,
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(previouslyInstalledBytes)).ToLowerInvariant(),
                InstalledAt = DateTimeOffset.UtcNow
            });

        await fixture.InstallForTestAsync();

        Assert.Equal(installedBytes, await File.ReadAllBytesAsync(gameFile));
        var state = new InstallationStateStore(
            fixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id),
            new MainFormTestFixture.TestLogger()).Load();
        Assert.Equal(FileLoadStatus.Valid, state.Status);
        Assert.Equal("01FRESHB", state.Value!.PublicId);
        Assert.Contains("/download/releases/01FRESHB", handler.RequestPaths);
        Assert.DoesNotContain("/download/releases/01STALEA", handler.RequestPaths);
    }

    [Fact]
    public async Task InstalledMode_UsesPerModeLatestForUpdateAndCannotRegressToStaleAggregate()
    {
        var installedA = Encoding.UTF8.GetBytes("installed release A");
        var latestB = Encoding.UTF8.GetBytes("authoritative release B");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01RELEASEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([JsonResponse(HttpStatusCode.OK, CreateLatestJson("01RELEASEB", latestB, "verified"), "\"release-b\""),
                new HttpResponseMessage(HttpStatusCode.NotModified),
                JsonResponse(HttpStatusCode.OK, CreateLatestJson("01RELEASEB", latestB, "verified"), "\"release-b\""),
                JsonResponse(HttpStatusCode.OK, CreateLatestJson("01RELEASEB", latestB, "verified"), "\"release-b\"")]),
            latestB);
        using var fixture = await MainFormTestFixture.StartAsync(
            handler,
            gamePatch: 395,
            beforeUiThreadStart: async currentFixture =>
            {
                var path = BdoGameDefinition.Default.GetLocalizationFilePath(currentFixture.GameRoot);
                var paths = currentFixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id);
                paths.EnsureDirectories();
                var logger = new MainFormTestFixture.TestLogger();
                var backupStore = new BackupStore(paths, logger);
                var snapshot = await backupStore.CreateOriginalSnapshotAsync(currentFixture.GameRoot, 395);
                Assert.True(snapshot.IsSuccess, snapshot.ErrorMessage);
                await File.WriteAllBytesAsync(path, installedA);
                await new InstallationStateStore(paths, logger).SaveAsync(new InstallationMetadata
                {
                    ModeSlug = "english-items",
                    PublicId = "01RELEASEA",
                    Version = 1,
                    GamePatch = 395,
                    Sha256 = Convert.ToHexString(SHA256.HashData(installedA)).ToLowerInvariant(),
                    InstalledAt = DateTimeOffset.UtcNow
                });
            });

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.SelectedModeCardActionTextForTest == "Оновити");
        Assert.Equal("Доступне оновлення", fixture.Form.SelectedModeCardStateTextForTest);
        Assert.True(fixture.Form.SelectedModeCardActionEnabledForTest);
        Assert.Equal(installedA, await File.ReadAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot)));

        await fixture.InstallForTestAsync();

        var gameFile = BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot);
        Assert.Equal(latestB, await File.ReadAllBytesAsync(gameFile));
        var state = new InstallationStateStore(
            fixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id),
            new MainFormTestFixture.TestLogger()).Load();
        Assert.Equal(FileLoadStatus.Valid, state.Status);
        Assert.Equal("01RELEASEB", state.Value!.PublicId);
        Assert.Equal(2, state.Value.Version);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(latestB)).ToLowerInvariant(), state.Value.Sha256);
        Assert.Equal("✓ Встановлено", fixture.Form.SelectedModeCardStateTextForTest);
        Assert.Null(fixture.Form.SelectedModeCardActionTextForTest);
        Assert.Equal(4, handler.RequestPaths.Count(path => path.EndsWith("/releases/latest/english-items", StringComparison.Ordinal)));
        Assert.Single(handler.RequestPaths.Where(path => path == "/download/releases/01RELEASEB"));
        Assert.Contains(fixture.Logger.Warnings, message => message.Contains("latest/aggregate identity conflict", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FirstInstall_StaleAggregatePatchDoesNotBlockAuthoritativeLatestAtGamePatch()
    {
        var payload = Encoding.UTF8.GetBytes("authoritative patch 396 localization");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01STALEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([JsonResponse(HttpStatusCode.OK, CreateLatestJson("01LATESTB", payload, "verified", patch: 396), "\"latest-b\""),
                JsonResponse(HttpStatusCode.OK, CreateLatestJson("01LATESTB", payload, "verified", patch: 396), "\"latest-b\"")]),
            payload);
        using var fixture = await MainFormTestFixture.StartAsync(handler, gamePatch: 396);
        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.SelectedModeCardActionTextForTest == "Встановити");

        await fixture.InstallForTestAsync();

        Assert.Equal(payload, await File.ReadAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot)));
        var state = new InstallationStateStore(
            fixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id),
            new MainFormTestFixture.TestLogger()).Load();
        Assert.Equal("01LATESTB", state.Value!.PublicId);
        Assert.Equal(396, state.Value.GamePatch);
        Assert.Equal("✓ Встановлено", fixture.Form.SelectedModeCardStateTextForTest);
    }

    [Fact]
    public async Task InstalledMode_StaleAggregatePatchUsesAuthoritativePatchForUpdateEligibility()
    {
        var installedA = Encoding.UTF8.GetBytes("installed release A");
        var latestB = Encoding.UTF8.GetBytes("latest release B at game patch");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01RELEASEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([JsonResponse(HttpStatusCode.OK, CreateLatestJson("01RELEASEB", latestB, "verified", patch: 396), "\"release-b\"")]),
            latestB);
        using var fixture = await MainFormTestFixture.StartAsync(
            handler,
            gamePatch: 396,
            beforeUiThreadStart: async currentFixture =>
            {
                var path = BdoGameDefinition.Default.GetLocalizationFilePath(currentFixture.GameRoot);
                var paths = currentFixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id);
                paths.EnsureDirectories();
                var logger = new MainFormTestFixture.TestLogger();
                var snapshot = await new BackupStore(paths, logger).CreateOriginalSnapshotAsync(currentFixture.GameRoot, 395);
                Assert.True(snapshot.IsSuccess, snapshot.ErrorMessage);
                await File.WriteAllBytesAsync(path, installedA);
                await new InstallationStateStore(paths, logger).SaveAsync(new InstallationMetadata
                {
                    ModeSlug = "english-items", PublicId = "01RELEASEA", Version = 1, GamePatch = 395,
                    Sha256 = Convert.ToHexString(SHA256.HashData(installedA)).ToLowerInvariant(),
                    InstalledAt = DateTimeOffset.UtcNow
                });
            });

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.SelectedModeCardActionTextForTest == "Оновити");

        Assert.Equal("Доступне оновлення", fixture.Form.SelectedModeCardStateTextForTest);
        Assert.True(fixture.Form.SelectedModeCardActionEnabledForTest);
        Assert.Equal(installedA, await File.ReadAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot)));
    }

    [Fact]
    public async Task AuthoritativeLatestPatchBehindGameStillBlocksInstall()
    {
        var payload = Encoding.UTF8.GetBytes("too old localization");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01STALEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([JsonResponse(HttpStatusCode.OK, CreateLatestJson("01OLD", payload, "verified", patch: 395), "\"old\"")]),
            payload);
        using var fixture = await MainFormTestFixture.StartAsync(handler, gamePatch: 396);
        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.SelectedModeCardActionTextForTest == "Встановити");
        var gameFile = BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot);
        var originalBytes = await File.ReadAllBytesAsync(gameFile);

        await fixture.InstallForTestAsync();

        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(gameFile));
        Assert.Equal(FileLoadStatus.Missing, new InstallationStateStore(
            fixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id),
            new MainFormTestFixture.TestLogger()).Load().Status);
        Assert.DoesNotContain(handler.RequestPaths, path => path.StartsWith("/download/", StringComparison.Ordinal));
        Assert.Equal(GamePatchStatus.NewerThanLatestLocalization, fixture.Form.GamePatchStatusForTest);
    }

    [Fact]
    public async Task LatestFailureCanRecoverInSameSessionWithoutAggregateMutationFallback()
    {
        var payload = Encoding.UTF8.GetBytes("release after temporary API failure");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01AGGREGATEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([JsonResponse(HttpStatusCode.BadGateway, "temporary latest failure"),
                JsonResponse(HttpStatusCode.OK, CreateLatestJson("01LATESTB", payload, "verified"), "\"latest-b\""),
                JsonResponse(HttpStatusCode.OK, CreateLatestJson("01LATESTB", payload, "verified"), "\"latest-b\""),
                JsonResponse(HttpStatusCode.OK, CreateLatestJson("01LATESTB", payload, "verified"), "\"latest-b\"")]),
            payload);
        using var fixture = await MainFormTestFixture.StartAsync(handler, gamePatch: 395);
        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => MainFormTestFixture.CountModeCards(form) == 1);
        var gameFile = BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot);
        var originalBytes = await File.ReadAllBytesAsync(gameFile);

        await fixture.InstallForTestAsync();
        Assert.DoesNotContain(handler.RequestPaths, path => path.StartsWith("/download/", StringComparison.Ordinal));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(gameFile));

        await fixture.Form.RefreshRelevantLatestForTestAsync();
        await fixture.WaitForAsync(form => form.SelectedModeCardActionTextForTest == "Встановити");
        await fixture.InstallForTestAsync();

        Assert.Equal(payload, await File.ReadAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot)));
        Assert.Equal("01LATESTB", new InstallationStateStore(
            fixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id),
            new MainFormTestFixture.TestLogger()).Load().Value!.PublicId);
        Assert.Single(handler.RequestPaths.Where(path => path == "/download/releases/01LATESTB"));
    }

    [Fact]
    public async Task InstalledMode_DetectsNewPerModeLatestWhileApplicationRemainsRunning()
    {
        var installedA = Encoding.UTF8.GetBytes("installed release A");
        var latestB = Encoding.UTF8.GetBytes("new latest release B");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01RELEASEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([JsonResponse(HttpStatusCode.OK, CreateLatestJson("01RELEASEA", installedA, "verified"), "\"release-a\""),
                JsonResponse(HttpStatusCode.OK, CreateLatestJson("01RELEASEB", latestB, "verified"), "\"release-b\"")]),
            latestB);
        using var fixture = await MainFormTestFixture.StartAsync(
            handler,
            gamePatch: 395,
            beforeUiThreadStart: async currentFixture =>
            {
                var path = BdoGameDefinition.Default.GetLocalizationFilePath(currentFixture.GameRoot);
                var paths = currentFixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id);
                paths.EnsureDirectories();
                var logger = new MainFormTestFixture.TestLogger();
                var snapshot = await new BackupStore(paths, logger).CreateOriginalSnapshotAsync(currentFixture.GameRoot, 395);
                Assert.True(snapshot.IsSuccess, snapshot.ErrorMessage);
                await File.WriteAllBytesAsync(path, installedA);
                await new InstallationStateStore(paths, logger).SaveAsync(new InstallationMetadata
                {
                    ModeSlug = "english-items", PublicId = "01RELEASEA", Version = 1, GamePatch = 395,
                    Sha256 = Convert.ToHexString(SHA256.HashData(installedA)).ToLowerInvariant(),
                    InstalledAt = DateTimeOffset.UtcNow
                });
            });

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.SelectedModeCardStateTextForTest == "✓ Встановлено");
        await fixture.RefreshInstalledModeLatestAsync();

        Assert.Equal("Доступне оновлення", fixture.Form.SelectedModeCardStateTextForTest);
        Assert.Equal("Оновити", fixture.Form.SelectedModeCardActionTextForTest);
        Assert.True(fixture.Form.SelectedModeCardActionEnabledForTest);
        Assert.Equal(installedA, await File.ReadAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot)));
    }

    [Fact]
    public async Task Conflicting304ThenFailedUnconditionalRevalidation_BlocksMutation()
    {
        var installedA = Encoding.UTF8.GetBytes("installed release A");
        var latestB = Encoding.UTF8.GetBytes("authoritative release B");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01RELEASEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([JsonResponse(HttpStatusCode.OK, CreateLatestJson("01RELEASEB", latestB, "verified"), "\"release-b\""),
                new HttpResponseMessage(HttpStatusCode.NotModified),
                JsonResponse(HttpStatusCode.InternalServerError, "revalidation failed")]),
            latestB);
        using var fixture = await MainFormTestFixture.StartAsync(
            handler,
            gamePatch: 395,
            beforeUiThreadStart: async currentFixture =>
            {
                var path = BdoGameDefinition.Default.GetLocalizationFilePath(currentFixture.GameRoot);
                var paths = currentFixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id);
                paths.EnsureDirectories();
                var logger = new MainFormTestFixture.TestLogger();
                var snapshot = await new BackupStore(paths, logger).CreateOriginalSnapshotAsync(currentFixture.GameRoot, 395);
                Assert.True(snapshot.IsSuccess, snapshot.ErrorMessage);
                await File.WriteAllBytesAsync(path, installedA);
                await new InstallationStateStore(paths, logger).SaveAsync(new InstallationMetadata
                {
                    ModeSlug = "english-items", PublicId = "01RELEASEA", Version = 1, GamePatch = 395,
                    Sha256 = Convert.ToHexString(SHA256.HashData(installedA)).ToLowerInvariant(),
                    InstalledAt = DateTimeOffset.UtcNow
                });
            });

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => form.SelectedModeCardActionTextForTest == "Оновити");
        await fixture.InstallForTestAsync();

        Assert.Equal(installedA, await File.ReadAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot)));
        Assert.Equal("01RELEASEA", new InstallationStateStore(
            fixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id),
            new MainFormTestFixture.TestLogger()).Load().Value!.PublicId);
        Assert.Null(fixture.Form.SelectedModeCardActionTextForTest);
        Assert.DoesNotContain(handler.RequestPaths, path => path.StartsWith("/download/", StringComparison.Ordinal));
        Assert.Contains(fixture.Logger.Warnings, message => message.Contains("Aggregate metadata will not be used as fallback", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_304UsesCachedFreshTargetAndDoesNotReinstallExactInstalledRelease()
    {
        var installedBytes = Encoding.UTF8.GetBytes("latest release B");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01STALEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([JsonResponse(HttpStatusCode.OK, CreateLatestJson("01FRESHB", installedBytes, "verified"), "\"latest-b\""),
                new HttpResponseMessage(HttpStatusCode.NotModified),
                JsonResponse(HttpStatusCode.OK, CreateLatestJson("01FRESHB", installedBytes, "verified"), "\"latest-b\""),
                new HttpResponseMessage(HttpStatusCode.NotModified),
                JsonResponse(HttpStatusCode.OK, CreateLatestJson("01FRESHB", installedBytes, "verified"), "\"latest-b\"")]),
            installedBytes);
        using var fixture = await MainFormTestFixture.StartAsync(handler, gamePatch: 395);
        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => MainFormTestFixture.CountModeCards(form) == 1);

        await fixture.InstallForTestAsync();
        await fixture.InstallForTestAsync();

        Assert.Equal(5, handler.RequestPaths.Count(path => path.EndsWith("/releases/latest/english-items", StringComparison.Ordinal)));
        Assert.Single(handler.RequestPaths.Where(path => path == "/download/releases/01FRESHB"));
        Assert.Equal(installedBytes, await File.ReadAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot)));
        Assert.Equal(OperationState.Idle, fixture.Form.OperationStateForTest);
    }

    [Theory]
    [InlineData("unknown-mode")]
    [InlineData("patch-unconfirmed")]
    [InlineData("server-error")]
    [InlineData("invalid-json")]
    [InlineData("current-null")]
    [InlineData("incompatible")]
    public async Task Install_LatestFailureBlocksBeforeAnyMutation(string scenario)
    {
        var originalBytes = Encoding.UTF8.GetBytes("original game file");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01AGGREGATEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([scenario switch
            {
                "unknown-mode" => JsonResponse(HttpStatusCode.NotFound,
                    "{\"success\":false,\"error\":\"unknown_mode\",\"allowed\":[\"english-items\"]}"),
                "patch-unconfirmed" => JsonResponse(HttpStatusCode.ServiceUnavailable,
                    "{\"success\":false,\"error\":\"official_patch_unconfirmed\"}"),
                "server-error" => JsonResponse(HttpStatusCode.InternalServerError, "server error"),
                "current-null" => JsonResponse(HttpStatusCode.OK, CreateLatestNoCurrentJson()),
                "incompatible" => JsonResponse(HttpStatusCode.OK,
                    CreateLatestJson("01INCOMPATIBLE", Array.Empty<byte>(), "verified", compatible: false)),
                _ => JsonResponse(HttpStatusCode.OK, "not json")
            }]),
            Array.Empty<byte>());
        using var fixture = await MainFormTestFixture.StartAsync(handler, gamePatch: 395);
        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => MainFormTestFixture.CountModeCards(form) == 1);
        await File.WriteAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot), originalBytes);

        await fixture.InstallForTestAsync();

        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot)));
        Assert.DoesNotContain(handler.RequestPaths, path => path.StartsWith("/download/", StringComparison.Ordinal));
        Assert.Equal(FileLoadStatus.Missing, new InstallationStateStore(
            fixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id),
            new MainFormTestFixture.TestLogger()).Load().Status);
        if (scenario == "incompatible")
            Assert.Equal(OperationState.Idle, fixture.Form.OperationStateForTest);
    }

    [Fact]
    public async Task Install_DeclinedGameTestWarningBlocksMutationAndAcceptedWarningContinues()
    {
        var originalBytes = Encoding.UTF8.GetBytes("original game file");
        var payload = Encoding.UTF8.GetBytes("release with issues");
        var handler = CreateInstallHandler(
            aggregatePublicId: "01AGGREGATEA",
            latestResponses: new Queue<HttpResponseMessage>
            ([JsonResponse(HttpStatusCode.OK, CreateLatestJson("01ISSUES", payload, "known_issues"), "\"issues\""),
                new HttpResponseMessage(HttpStatusCode.NotModified),
                JsonResponse(HttpStatusCode.OK, CreateLatestJson("01ISSUES", payload, "known_issues"), "\"issues\""),
                JsonResponse(HttpStatusCode.OK, CreateLatestJson("01ISSUES", payload, "known_issues"), "\"issues\"")]),
            payload);
        using var fixture = await MainFormTestFixture.StartAsync(handler, gamePatch: 395);
        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => MainFormTestFixture.CountModeCards(form) == 1);
        await File.WriteAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot), originalBytes);

        string? stateDuringConfirmation = null;
        await fixture.InstallForTestAsync(_ =>
        {
            stateDuringConfirmation = fixture.Form.SelectedModeCardStateTextForTest;
            return false;
        });

        Assert.Equal("Очікує підтвердження", stateDuringConfirmation);
        Assert.Null(fixture.Form.SelectedModeCardStateTextForTest);

        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot)));
        Assert.DoesNotContain(handler.RequestPaths, path => path.StartsWith("/download/", StringComparison.Ordinal));
        Assert.Equal(FileLoadStatus.Missing, new InstallationStateStore(
            fixture.AppPaths.GetGamePersistencePaths(BdoGameDefinition.Default.Id),
            new MainFormTestFixture.TestLogger()).Load().Status);

        await fixture.InstallForTestAsync(_ => true);

        Assert.Equal(payload, await File.ReadAllBytesAsync(
            BdoGameDefinition.Default.GetLocalizationFilePath(fixture.GameRoot)));
        Assert.Contains("/download/releases/01ISSUES", handler.RequestPaths);
    }

    private static MainFormTestHttpHandler CreateInstallHandler(
        string aggregatePublicId,
        Queue<HttpResponseMessage> latestResponses,
        byte[] downloadBytes)
    {
        return new MainFormTestHttpHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/public/v1/releases")
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, CreateAggregateJson(aggregatePublicId)));
            if (path == "/api/public/v1/releases/latest/english-items")
            {
                if (latestResponses.Count == 0)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                return Task.FromResult(latestResponses.Dequeue());
            }
            if (path.StartsWith("/download/releases/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(downloadBytes)
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }

    private static string CreateAggregateJson(string publicId) => JsonSerializer.Serialize(new
    {
        success = true,
        data = new
        {
            official_patch = 395,
            official_source_url = "https://bdo-ua.com.ua/original.loc",
            filename = "languagedata_en.loc",
            install_guide_url = "https://bdo-ua.com.ua/download",
            modes = new[]
            {
                new
                {
                    slug = "english-items",
                    public_name = "Англійські назви предметів",
                    description = "Test mode",
                    audience = "Everyone",
                    current = new
                    {
                        public_id = publicId,
                        version = 1,
                        filename = "languagedata_en.loc",
                        download_url = $"https://bdo-ua.com.ua/download/releases/{publicId}",
                        size_bytes = 1,
                        sha256 = new string('a', 64),
                        patch = 395,
                        compatible_with_official_patch = true,
                        game_test = new { state = "verified" }
                    }
                }
            }
        }
    });

    private static string CreateLatestJson(string publicId, byte[] bytes, string gameTestState, bool compatible = true, int patch = 395) => JsonSerializer.Serialize(new
    {
        success = true,
        generated_at = "2026-10-04T12:00:00+03:00",
        data = new
        {
            official_patch = 395,
            filename = "languagedata_en.loc",
            install_guide_url = "https://bdo-ua.com.ua/download",
            mode = new { slug = "english-items", public_name = "Англійські назви предметів" },
            current = new
            {
                public_id = publicId,
                version = 2,
                filename = "languagedata_en.loc",
                download_url = $"https://bdo-ua.com.ua/download/releases/{publicId}",
                size_bytes = bytes.Length,
                sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                patch,
                compatible_with_official_patch = compatible,
                game_test = new { state = gameTestState, label = "Server label", note = "Server note" }
            }
        }
    });

    private static string CreateLatestNoCurrentJson() => JsonSerializer.Serialize(new
    {
        success = true,
        generated_at = "2026-10-04T12:00:00+03:00",
        data = new
        {
            official_patch = 395,
            filename = "languagedata_en.loc",
            install_guide_url = "https://bdo-ua.com.ua/download",
            mode = new { slug = "english-items", public_name = "Англійські назви предметів" },
            current = (CurrentRelease?)null
        }
    });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body, string? etag = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (etag != null)
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return response;
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
    private readonly bool _includeWwm;
    private readonly Func<HttpMessageHandler>? _wwmHandlerFactory;
    private readonly bool _seedMalformedWwmJournal;
    private readonly bool _wwmHasSteamManifest;
    private readonly bool _wwmUsesLocalDataPatchLayout;
    private readonly bool _wwmHasEpicManifest;
    private readonly bool _wwmAlsoHasLocalDataPatchLayout;
    private readonly bool _wwmGameMarkersPresent;
    private readonly Func<string, IEnumerable<string>>? _wwmManualDirectoryEnumerator;
    private readonly bool _wwmConfigWithoutGamePath;
    private readonly bool _seedLegacyWwmState;
    private readonly string _wwmSteamBuildId;
    private readonly bool _wwmInvalidConfigInitially;
    private readonly TaskCompletionSource<MainForm> _formReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<object?> _hostCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Thread _uiThread;
    private Exception? _hostException;
    private TestLogger? _logger;
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
        bool includeSyntheticSecondGame,
        bool includeWwm,
        Func<HttpMessageHandler>? wwmHandlerFactory,
        bool seedMalformedWwmJournal,
        string? wwmConfigJson,
        bool wwmHasSteamManifest,
        bool wwmUsesLocalDataPatchLayout,
        bool wwmGameMarkersPresent,
        Func<string, IEnumerable<string>>? wwmManualDirectoryEnumerator,
        bool wwmHasEpicManifest,
        bool wwmAlsoHasLocalDataPatchLayout,
        bool wwmConfigWithoutGamePath,
        bool seedLegacyWwmState,
        string wwmSteamBuildId,
        bool wwmInvalidConfigInitially)
    {
        _bdoHandler = bdoHandler;
        _includeSyntheticSecondGame = includeSyntheticSecondGame;
        _includeWwm = includeWwm;
        _wwmHandlerFactory = wwmHandlerFactory;
        _seedMalformedWwmJournal = seedMalformedWwmJournal;
        _wwmHasSteamManifest = wwmHasSteamManifest;
        _wwmUsesLocalDataPatchLayout = wwmUsesLocalDataPatchLayout;
        _wwmHasEpicManifest = wwmHasEpicManifest;
        _wwmAlsoHasLocalDataPatchLayout = wwmAlsoHasLocalDataPatchLayout;
        _wwmGameMarkersPresent = wwmGameMarkersPresent;
        _wwmManualDirectoryEnumerator = wwmManualDirectoryEnumerator;
        _wwmConfigWithoutGamePath = wwmConfigWithoutGamePath;
        _seedLegacyWwmState = seedLegacyWwmState;
        _wwmSteamBuildId = wwmSteamBuildId;
        _wwmInvalidConfigInitially = wwmInvalidConfigInitially;
        if (includeWwm && wwmHandlerFactory == null)
            throw new ArgumentException("A WWM API handler factory is required when WWM is included.", nameof(wwmHandlerFactory));
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

        _wwmSteamRoot = Path.Combine(_root, "fake-steam-library");
        WwmGameRoot = Path.Combine(_wwmSteamRoot, "steamapps", "common", "fake-wwm-game");
        if (includeWwm)
        {
            var definition = WwmGameDefinition.Default;
            if (wwmGameMarkersPresent)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(WwmGameRoot, definition.ExecutableRelativePath))!);
                File.WriteAllBytes(Path.Combine(WwmGameRoot, definition.ExecutableRelativePath), new byte[] { 1 });
                Directory.CreateDirectory(Path.Combine(WwmGameRoot,
                    wwmUsesLocalDataPatchLayout ? definition.LocalDataPatchLocaleRelativePath : definition.LocaleRelativePath));
                if (wwmAlsoHasLocalDataPatchLayout)
                    Directory.CreateDirectory(Path.Combine(WwmGameRoot,
                        wwmUsesLocalDataPatchLayout ? definition.LocaleRelativePath : definition.LocalDataPatchLocaleRelativePath));
                if (wwmHasSteamManifest && !wwmUsesLocalDataPatchLayout)
                {
                    Directory.CreateDirectory(Path.Combine(_wwmSteamRoot, "steamapps"));
                    File.WriteAllText(Path.Combine(_wwmSteamRoot, "steamapps", $"appmanifest_{definition.SteamAppId}.acf"),
                        $"\"AppState\" {{ \"appid\" \"{definition.SteamAppId}\" \"installdir\" \"fake-wwm-game\" \"buildid\" \"{_wwmSteamBuildId}\" }}");
                }
            }
            var wwmPaths = _appPaths.GetGamePersistencePaths(definition.Id);
            wwmPaths.EnsureDirectories();
            if (_wwmHasEpicManifest)
            {
                var epicManifests = Path.Combine(_root, "epic-manifests");
                Directory.CreateDirectory(epicManifests);
                File.WriteAllText(Path.Combine(epicManifests, "wwm.item"),
                    JsonSerializer.Serialize(new { AppName = "WhereWindsMeet", DisplayName = "Where Winds Meet", InstallLocation = WwmGameRoot }));
            }
            var wwmConfig = wwmConfigJson == null ? new Config() : JsonSerializer.Deserialize<Config>(wwmConfigJson) ?? new Config();
            if (!_wwmConfigWithoutGamePath) wwmConfig.GamePath ??= WwmGameRoot;
            File.WriteAllText(wwmPaths.ConfigFile, _wwmInvalidConfigInitially
                ? "{ invalid configuration"
                : JsonSerializer.Serialize(wwmConfig));
            if (_seedLegacyWwmState)
                SeedLegacyWwmState(wwmPaths, definition, WwmGameRoot);
            if (_seedMalformedWwmJournal)
            {
                var journal = Path.Combine(wwmPaths.StateDir, "wwm-transaction", "journal.json");
                Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
                File.WriteAllText(journal, "{ invalid journal");
            }
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

    private static void SeedLegacyWwmState(GamePersistencePaths paths, WwmGameDefinition definition, string gameRoot)
    {
        var targets = definition.GetManagedRelativePaths(WwmInstallationLayout.Package);
        var installed = new[] { new byte[] { 10, 11, 12 }, new byte[] { 13, 14, 15, 16 } };
        var baseline = new[] { new byte[] { 90, 91 }, new byte[] { 92, 93, 94 } };
        for (var index = 0; index < targets.Length; index++)
            File.WriteAllBytes(Path.Combine(gameRoot, targets[index]), installed[index]);

        var cycleId = Guid.NewGuid().ToString("N");
        var state = new WwmInstallationState
        {
            SchemaVersion = 1, ModeSlug = "ukrainian", ModeVariant = "default", ReleaseVersion = "v2.6.3",
            ArchiveSizeBytes = 1, ArchiveSha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1 })).ToLowerInvariant(),
            PreHubSnapshotCycleId = cycleId, GameBuildId = "25281753", InstalledAt = DateTimeOffset.UtcNow,
            Targets = installed.Select((bytes, index) => new WwmTargetManifestEntry
            {
                RelativePath = targets[index], SizeBytes = bytes.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
            }).ToList()
        };
        var store = new WwmStateStore(paths);
        store.SaveAsync(state).GetAwaiter().GetResult();
        Directory.CreateDirectory(store.SnapshotDirectory);
        var snapshotTargets = new List<WwmPreviousTarget>();
        for (var index = 0; index < targets.Length; index++)
        {
            var backupName = $"{index}.bin";
            File.WriteAllBytes(Path.Combine(store.SnapshotDirectory, backupName), baseline[index]);
            snapshotTargets.Add(new WwmPreviousTarget
            {
                RelativePath = targets[index], Existed = true, SizeBytes = baseline[index].Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(baseline[index])).ToLowerInvariant(), BackupFile = backupName
            });
        }
        File.WriteAllText(Path.Combine(store.SnapshotDirectory, "snapshot.json"), JsonSerializer.Serialize(
            new WwmPreHubSnapshot { CycleOperationId = cycleId, GameBuildId = "25281753", Targets = snapshotTargets }));
    }

    internal string GameRoot { get; }
    internal string WwmGameRoot { get; }
    internal string WwmSelectedParentForTest => _wwmSteamRoot;
    private string _wwmSteamRoot { get; }
    internal TestLogger Logger => _logger ?? throw new InvalidOperationException("Test logger is not ready.");

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

    internal async Task<Task<WwmManualDetectionResult?>> BeginWwmManualPathSearchAsync(
        string selectedPath, CancellationToken token = default)
    {
        var completion = new TaskCompletionSource<Task<WwmManualDetectionResult?>>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(() =>
        {
            try { completion.TrySetResult(Form.ResolveWwmManualPathForTestAsync(selectedPath, token)); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        return await completion.Task.WaitAsync(Timeout);
    }

    internal async Task InvokeWwmInstallAsync(string slug, string variant, Func<CancellationToken, Task<WwmMutationResult>> install)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(async () =>
        {
            try
            {
                Form.WwmInstallResultForTest = install;
                await Form.HandleWwmInstallForTestAsync(slug, variant);
                completion.TrySetResult(null);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        await completion.Task.WaitAsync(Timeout);
    }

    internal async Task<WwmMutationResult> InvokeWwmRestoreAsync()
    {
        var completion = new TaskCompletionSource<WwmMutationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(async () =>
        {
            try { completion.TrySetResult(await Form.HandleWwmRestoreForTestAsync()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        return await completion.Task.WaitAsync(Timeout);
    }

    internal async Task InvokeDetectGameAsync()
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(async () =>
        {
            try { await Form.DetectGameForTestAsync(); completion.TrySetResult(null); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        await completion.Task.WaitAsync(Timeout);
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

    internal async Task InstallForTestAsync(Func<CurrentRelease, bool>? confirmGameTest = null)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(async () =>
        {
            try
            {
                Form.GameTestConfirmationForTest = confirmGameTest;
                await Form.HandleInstallForTestAsync();
                completion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        await completion.Task.WaitAsync(Timeout);
    }

    internal async Task RefreshInstalledModeLatestAsync()
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(async () =>
        {
            try { await Form.RefreshInstalledModeLatestForTestAsync(); completion.TrySetResult(null); }
            catch (Exception ex) { completion.TrySetException(ex); }
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
        bool includeSyntheticSecondGame = false,
        bool includeWwm = false,
        Func<HttpMessageHandler>? wwmHandlerFactory = null,
        bool seedMalformedWwmJournal = false,
        string? wwmConfigJson = null,
        bool wwmHasSteamManifest = true,
        bool wwmUsesLocalDataPatchLayout = false,
        bool wwmGameMarkersPresent = true,
        Func<string, IEnumerable<string>>? wwmManualDirectoryEnumerator = null,
        bool wwmHasEpicManifest = false,
        bool wwmAlsoHasLocalDataPatchLayout = false,
        bool wwmConfigWithoutGamePath = false,
        bool wwmInvalidConfigInitially = false,
        bool seedLegacyWwmState = false,
        string wwmSteamBuildId = "25281753",
        Func<MainFormTestFixture, Task>? beforeUiThreadStart = null)
    {
        var fixture = new MainFormTestFixture(
            bdoHandler, startInBackground, exitWhenShown, seedReleaseFeedCache, gamePatch, applicationConfigJson,
            includeSyntheticSecondGame, includeWwm, wwmHandlerFactory, seedMalformedWwmJournal, wwmConfigJson,
            wwmHasSteamManifest, wwmUsesLocalDataPatchLayout, wwmGameMarkersPresent, wwmManualDirectoryEnumerator,
            wwmHasEpicManifest, wwmAlsoHasLocalDataPatchLayout, wwmConfigWithoutGamePath, seedLegacyWwmState,
            wwmSteamBuildId, wwmInvalidConfigInitially);
        try
        {
            if (beforeUiThreadStart != null) await beforeUiThreadStart(fixture);
            fixture._uiThread.Start();
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

    internal static Func<HttpMessageHandler> CreateWwmApiHandlerFactory(byte[]? package = null, List<string>? requests = null,
        Func<bool>? modeAvailable = null, HttpStatusCode latestStatus = HttpStatusCode.OK, bool omitCurrentArtifact = false,
        Func<bool?>? gameTested = null, Func<string?>? currentVersion = null)
        => () => new MainFormTestHttpHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requests?.Add(path);
            if (path == "/api/public/v1/releases/latest")
                return Task.FromResult(new HttpResponseMessage(latestStatus)
                {
                    Content = new StringContent(latestStatus == HttpStatusCode.OK
                        ? CreateWwmLatestJson(package, modeAvailable?.Invoke() ?? package != null, omitCurrentArtifact,
                            gameTested == null ? true : gameTested.Invoke(),
                            currentVersion == null ? "v-test" : currentVersion.Invoke())
                        : "{\"success\":false,\"error\":\"temporary_failure\"}", Encoding.UTF8, "application/json")
                });
            if (path == "/releases/test.zip" && package != null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

    private static string CreateWwmLatestJson(byte[]? package, bool modeAvailable, bool omitCurrentArtifact, bool? gameTested,
        string? currentVersion = "v-test")
    {
        var bytes = package ?? Array.Empty<byte>();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        return JsonSerializer.Serialize(new
        {
            success = true,
            generated_at = "2026-10-09T00:00:00Z",
            data = new
            {
                current = new
                {
                    version = currentVersion, published_at = "2026-10-09T00:00:00Z", game_version = (string?)null,
                    game_tested = gameTested, translated_rows = (long?)null, status = "current",
                    files = modeAvailable && !omitCurrentArtifact
                        ? new object[] { new { variant = "default", slug = "ukrainian", label = "Українська",
                            download_url = "https://winds4ua.com.ua/releases/test.zip", size_bytes = bytes.Length, sha256 = hash } }
                        : Array.Empty<object>()
                },
                modes = new object[]
                {
                    new { variant = "default", slug = "ukrainian", label = "Українська", description = "", audience = "", available = modeAvailable,
                        download_url = modeAvailable ? "https://winds4ua.com.ua/releases/test.zip" : "", size_bytes = modeAvailable ? bytes.Length : (long?)null, sha256 = modeAvailable ? hash : null },
                    new { variant = "english_items", slug = "english-items", label = "Предмети англійською", description = "", audience = "", available = false,
                        download_url = "", size_bytes = (long?)null, sha256 = (string?)null },
                    new { variant = "english_terms", slug = "english-terms", label = "Терміни англійською", description = "", audience = "", available = false,
                        download_url = "", size_bytes = (long?)null, sha256 = (string?)null }
                }
            }
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
    }

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
            var gameStatus = FindControlText(form, text =>
                text == "✓ Гру знайдено"
                || text.StartsWith("✓ Гру знайдено • patch ", StringComparison.Ordinal));
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

    internal async Task RunWwmInstallAsync(string slug, string variant, bool confirm, WwmMutationResult? result = null,
        Action<string, string>? confirmationObserved = null)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(async () =>
        {
            Form.WwmCompatibilityConfirmationForTest = (title, body) =>
            {
                confirmationObserved?.Invoke(title, body);
                return confirm;
            };
            Form.WwmInstallResultForTest = result == null ? null : _ => Task.FromResult(result);
            try
            {
                await Form.HandleWwmInstallForTestAsync(slug, variant);
                completion.TrySetResult(null);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        await completion.Task.WaitAsync(Timeout);
    }

    internal async Task RefreshWwmStateAsync()
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostToUi(async () =>
        {
            try { await Form.RefreshWwmStateForTestAsync(); completion.TrySetResult(null); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        await completion.Task.WaitAsync(Timeout);
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

            var logger = _logger = new TestLogger();
            var applicationConfigStore = new ApplicationConfigStore(_appPaths, logger);
            var appVersionInfo = AppVersionInfo.FromRawVersion("1.2.2");
            _bdoSession = BdoGameSession.CreateForTests(
                _appPaths, logger, appVersionInfo, _bdoHttpClient);
            var descriptors = new List<GameDescriptor> { _bdoSession.Descriptor };
            if (_includeSyntheticSecondGame) descriptors.Add(new GameDescriptor("synthetic-game", "Synthetic Game"));
            if (_includeWwm) descriptors.Add(new GameDescriptor("where-winds-meet", "Where Winds Meet"));
            var gameCatalog = new GameCatalog(descriptors);
            _sessionHost = new SelectedGameSessionHost(
                _bdoSession,
                descriptor => descriptor.Id == "where-winds-meet"
                    ? WwmGameSession.CreateForTests(_appPaths, logger,
                        new HttpClient(_wwmHandlerFactory!(), disposeHandler: true), ownsHttpClient: true,
                        steamRoots: _wwmHasSteamManifest ? new[] { _wwmSteamRoot } : Array.Empty<string>(),
                        manualDirectoryEnumerator: _wwmManualDirectoryEnumerator,
                        epicManifestDirectory: _wwmHasEpicManifest ? Path.Combine(_root, "epic-manifests") : Path.Combine(_root, "no-epic-manifests"))
                    : BdoGameSession.CreateForTests(
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
        public List<string> Warnings { get; } = new();
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) => Warnings.Add(message);
        public void Error(string message) => Warnings.Add(message);
    }
}

internal sealed class MainFormTestHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _response;
    private readonly bool _waitForRelease;
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _send;
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

    internal MainFormTestHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        _statusCode = HttpStatusCode.OK;
        _response = string.Empty;
        _send = send;
    }

    internal HttpStatusCode StatusCode => _statusCode;

    internal int RequestCount => Volatile.Read(ref _requestCount);

    internal List<string> RequestPaths { get; } = [];

    internal Task RequestStarted => _requestStarted.Task;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
        if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal) == true)
        {
            Interlocked.Increment(ref _requestCount);
            _requestStarted.TrySetResult(null);

            if (_waitForRelease)
                await _release.Task.WaitAsync(cancellationToken);
        }

        if (_send != null)
        {
            var response = await _send(request, cancellationToken);
            response.RequestMessage ??= request;
            return response;
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
