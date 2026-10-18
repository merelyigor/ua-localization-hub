using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient.Tests.Services;

public sealed class WwmStage10Tests
{
    [Fact]
    public async Task ApiClient_ParsesCurrentNullAndUnknownFutureFieldsAndModes()
    {
        using var client = CreateClient(_ => JsonResponse("""
            {"success":true,"generated_at":"2026-10-09T00:00:00Z","future":{"x":1},"data":{"current":null,"modes":[{"variant":"default","slug":"ukrainian","label":"Українська","available":true,"future_mode_flag":true},{"variant":"english_items","slug":"english-items","label":"Items","available":false,"download_url":"","size_bytes":null,"sha256":null}]}}
            """));

        var result = await new Winds4UaApiClient(client, Logger).GetLatestAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Feed!.Data!.Current);
        Assert.Equal(2, result.Feed.Data.Modes!.Count);
        Assert.True(result.Feed.Data.Modes[0].Available);
        Assert.False(result.Feed.Data.Modes[1].Available);
    }

    [Fact]
    public async Task ApiClient_LogsReleaseVersionAndInstallableModes()
    {
        using var client = CreateClient(_ => JsonResponse("""
            {"success":true,"generated_at":"2026-10-18T20:05:00Z","data":{"current":{"version":"v2.6.7"},"modes":[{"variant":"default","slug":"ukrainian","label":"Українська","available":false},{"variant":"english_terms","slug":"english-terms","label":"Українська (Всі терміни англійською)","available":true}]}}
            """));
        var logger = new CapturingLogger();

        var result = await new Winds4UaApiClient(client, logger).GetLatestAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains(logger.Infos, line => line.Contains("current_version=v2.6.7", StringComparison.Ordinal));
        Assert.Contains(logger.Infos, line => line.Contains("available_modes=english-terms/english_terms", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"success\":false,\"data\":{\"modes\":[]}}")]
    [InlineData("not-json")]
    [InlineData("{\"success\":true,\"data\":{}}")]
    public async Task ApiClient_FailsClosedForInvalidFeed(string json)
    {
        using var client = CreateClient(_ => JsonResponse(json));
        var result = await new Winds4UaApiClient(client, Logger).GetLatestAsync();
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ApiClient_HandlesNetworkFailureAndTimeoutAsTypedFailure()
    {
        using var networkClient = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));
        var network = await new Winds4UaApiClient(networkClient, Logger).GetLatestAsync();
        Assert.False(network.IsSuccess);

        using var slowClient = new HttpClient(new DelayingHandler());
        var timeout = await new Winds4UaApiClient(slowClient, Logger, TimeSpan.FromMilliseconds(20)).GetLatestAsync();
        Assert.False(timeout.IsSuccess);
        Assert.False(timeout.IsCancelled);
    }

    [Fact]
    public async Task ApiClient_DistinguishesCallerCancellation()
    {
        using var client = new HttpClient(new DelayingHandler());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var result = await new Winds4UaApiClient(client, Logger, TimeSpan.FromSeconds(2)).GetLatestAsync(cancellation.Token);
        Assert.False(result.IsSuccess);
        Assert.True(result.IsCancelled);
    }

    [Fact]
    public void Resolver_UsesFreshModeIdentityAndMatchingCurrentFile()
    {
        var feed = Feed(available: true);
        Assert.True(WwmPackageResolver.TryResolve(feed, "ukrainian", "default", out var package, out _));
        Assert.Equal("v-test", package!.Version);
        Assert.Equal(2, package.SizeBytes);
        Assert.Equal("https://winds4ua.com.ua/package.zip", package.DownloadUrl);
    }

    [Fact]
    public void Resolver_IncludesAvailableModeWithValidInlineArtifactWhenCurrentFilesOmitsIt()
    {
        var feed = Feed(available: true);
        feed.Data!.Modes!.Add(new WwmMode
        {
            Slug = "english-terms",
            Variant = "english_terms",
            Available = true,
            DownloadUrl = "https://winds4ua.com.ua/releases/english-terms.zip",
            SizeBytes = 42,
            Sha256 = new string('b', 64)
        });

        Assert.True(WwmPackageResolver.TryResolve(feed, "english-terms", "english_terms", out var package, out var error), error);
        Assert.Equal("v-test", package!.Version);
        Assert.Equal("https://winds4ua.com.ua/releases/english-terms.zip", package.DownloadUrl);
        Assert.Equal(42, package.SizeBytes);
        Assert.Equal(new string('b', 64), package.Sha256);
    }

    [Fact]
    public void Resolver_RejectsConflictingInlineAndCurrentFileMetadata()
    {
        var feed = Feed(available: true);
        feed.Data!.Modes![0].DownloadUrl = "https://winds4ua.com.ua/other.zip";

        Assert.False(WwmPackageResolver.TryResolve(feed, "ukrainian", "default", out _, out _));
    }

    [Theory]
    [InlineData(false, "https://winds4ua.com.ua/package.zip", 2, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(true, "http://winds4ua.com.ua/package.zip", 2, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(true, "https://winds4ua.com.ua/package.zip", 0, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(true, "https://winds4ua.com.ua/package.zip", 2, "invalid")]
    public void Resolver_RejectsUnavailableOrInvalidSelectedPackage(bool available, string url, long size, string hash)
    {
        var feed = Feed(available);
        feed.Data!.Modes![0].DownloadUrl = url;
        feed.Data.Modes[0].SizeBytes = size;
        feed.Data.Modes[0].Sha256 = hash;
        feed.Data.Current!.Files![0].DownloadUrl = url;
        feed.Data.Current.Files[0].SizeBytes = size;
        feed.Data.Current.Files[0].Sha256 = hash;
        Assert.False(WwmPackageResolver.TryResolve(feed, "ukrainian", "default", out _, out _));
    }

    [Fact]
    public void Resolver_RejectsAvailableModeWithoutInlineOrCurrentArtifactMetadata()
    {
        var feed = Feed(available: true);
        feed.Data!.Current!.Files!.Clear();
        feed.Data.Modes![0].DownloadUrl = null;
        feed.Data.Modes[0].SizeBytes = null;
        feed.Data.Modes[0].Sha256 = null;
        Assert.False(WwmPackageResolver.TryResolve(feed, "ukrainian", "default", out _, out _));
    }

    [Fact]
    public void SteamDetector_UsesManifestAcrossLibrariesAndValidatesMarkers()
    {
        using var temp = new TestTemp();
        var steam = Directory.CreateDirectory(Path.Combine(temp.Root, "Steam")).FullName;
        var library = Directory.CreateDirectory(Path.Combine(temp.Root, "Library Two")).FullName;
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "WWM Custom"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"" + steam.Replace("\\", "\\\\") + "\" } \"1\" { \"path\" \"" + library.Replace("\\", "\\\\") + "\" } }");
        var root = Path.Combine(library, "steamapps", "common", "WWM Custom");
        CreateGameMarkers(root);
        File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_3564740.acf"),
            "\"AppState\" { \"appid\" \"3564740\" \"installdir\" \"WWM Custom\" \"buildid\" \"25281753\" }");

        var result = new WwmSteamDetector(Logger, new[] { steam }).Detect();

        Assert.Equal(Path.GetFullPath(root), result.GameRoot);
        Assert.Equal(WwmInstallationSource.Steam, result.Source);
        Assert.Equal(WwmInstallationLayout.Package, result.Layout);
        Assert.Equal("25281753", result.BuildId);
        Assert.Equal(Path.Combine(library, "steamapps", "appmanifest_3564740.acf"), result.ManifestPath);
    }

    [Fact]
    public void SteamDetector_RejectsWrongAppIdAndManualFolderWithoutProductMarkers()
    {
        using var temp = new TestTemp();
        var steam = Directory.CreateDirectory(Path.Combine(temp.Root, "Steam")).FullName;
        var library = Path.Combine(steam, "steamapps");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "libraryfolders.vdf"), "{}");
        File.WriteAllText(Path.Combine(library, "appmanifest_3564740.acf"), "\"appid\" \"999\" \"installdir\" \"Where Winds Meet\"");
        var detector = new WwmSteamDetector(Logger, new[] { steam });
        var namedFolder = Directory.CreateDirectory(Path.Combine(temp.Root, "Where Winds Meet")).FullName;
        Assert.False(detector.Detect().IsFound);
        Assert.False(detector.TryResolveManualRoot(namedFolder, out _));

        CreateGameMarkers(namedFolder);
        Assert.True(detector.TryResolveManualRoot(namedFolder, out var resolved));
        Assert.Equal(Path.GetFullPath(namedFolder), resolved);
    }

    [Fact]
    public void SteamDetector_RequiresManifestAndBothProductMarkers()
    {
        using var temp = new TestTemp();
        var steam = Path.Combine(temp.Root, "Steam");
        var library = Path.Combine(steam, "steamapps");
        var root = Path.Combine(library, "common", "Where Winds Meet");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(Path.Combine(root, WwmGameDefinition.Default.LocaleRelativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, WwmGameDefinition.Default.ExecutableRelativePath))!);
        File.WriteAllText(Path.Combine(library, "appmanifest_3564740.acf"),
            "\"appid\" \"3564740\" \"installdir\" \"Where Winds Meet\"");
        var detector = new WwmSteamDetector(Logger, new[] { steam });
        Assert.False(detector.Detect().IsFound);

        File.WriteAllBytes(Path.Combine(root, WwmGameDefinition.Default.ExecutableRelativePath), new byte[] { 1 });
        Directory.Delete(Path.Combine(root, WwmGameDefinition.Default.LocaleRelativePath), recursive: true);
        Assert.False(detector.Detect().IsFound);
    }

    [Fact]
    public void ManualPackageRootWithoutSteamEvidence_IsUnknownWindows()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "arbitrary-folder-name");
        CreateGameMarkers(root);
        var detector = new WwmSteamDetector(Logger, Array.Empty<string>());

        var resolution = detector.ResolveManualRoot(root);
        var result = detector.DescribeRoot(resolution.GamePath!, "manual-folder-selection");

        Assert.Equal(ManualResolveStatus.Found, resolution.Status);
        Assert.Equal(Path.GetFullPath(root), result.GameRoot);
        Assert.Equal(WwmInstallationSource.UnknownWindows, result.Source);
        Assert.Equal(WwmInstallationLayout.Package, result.Layout);
        Assert.True(result.IsMutationSupported);
    }

    [Fact]
    public void LocalDataPatchLayout_IsRecognizedAndCanBeSelectedForMutation()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "random-parent", "wwm_lite");
        CreateGameMarkers(root, localDataPatch: true);
        var definition = WwmGameDefinition.Default;

        Assert.True(definition.TryGetWindowsLayout(root, out var layout));
        Assert.Equal(WwmInstallationLayout.LocalDataPatch, layout);
        Assert.True(definition.ValidateGameRoot(root));
        var result = new WwmSteamDetector(Logger, Array.Empty<string>()).DescribeRoot(root, "manual-folder-selection");
        Assert.Equal(WwmInstallationSource.UnknownWindows, result.Source);
        Assert.Equal(WwmInstallationLayout.LocalDataPatch, result.TargetLayout);
        Assert.True(result.IsMutationSupported);
    }

    [Fact]
    public void EpicDetector_RequiresProductManifestAndValidatesArbitraryInstallLocation()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "arbitrary", "install", "not-a-known-folder");
        CreateGameMarkers(root);
        var manifests = Path.Combine(temp.Root, "epic-manifests");
        Directory.CreateDirectory(manifests);
        File.WriteAllText(Path.Combine(manifests, "valid.item"), System.Text.Json.JsonSerializer.Serialize(new
        {
            AppName = "WhereWindsMeet", DisplayName = "Where Winds Meet", InstallLocation = root + Path.DirectorySeparatorChar
        }));
        var other = Path.Combine(temp.Root, "other-game");
        CreateGameMarkers(other);
        File.WriteAllText(Path.Combine(manifests, "other.item"), System.Text.Json.JsonSerializer.Serialize(new
        {
            AppName = "AnotherGame", DisplayName = "Another Game", InstallLocation = other
        }));

        var detector = new WwmSteamDetector(Logger, Array.Empty<string>(), null,
            path => Directory.EnumerateDirectories(path), epicManifestDirectory: manifests);

        var result = detector.Detect();

        Assert.Equal(Path.GetFullPath(root), result.GameRoot);
        Assert.Equal(WwmInstallationSource.EpicGames, result.Source);
        Assert.Equal(WwmInstallationLayout.Package, result.TargetLayout);
        Assert.True(result.IsMutationSupported);
        Assert.Equal(WwmInstallationSource.EpicGames,
            detector.DescribeRoot(root, "saved-config").Source);
    }

    [Fact]
    public void AutomaticSteamAndEpicCandidates_AreAmbiguousButExplicitSavedRootIsReproven()
    {
        using var temp = new TestTemp();
        var (steamRoot, steamGameRoot) = CreateSteamRoot(temp.Root, "steam", "custom-steam-dir");
        var epicGameRoot = Path.Combine(temp.Root, "unrelated", "nested", "custom-epic-dir");
        CreateGameMarkers(epicGameRoot);
        var epicManifests = Path.Combine(temp.Root, "epic-manifests");
        Directory.CreateDirectory(epicManifests);
        File.WriteAllText(Path.Combine(epicManifests, "wwm.item"), System.Text.Json.JsonSerializer.Serialize(
            new { AppName = "WhereWindsMeet", InstallLocation = epicGameRoot }));
        var detector = new WwmSteamDetector(Logger, new[] { steamRoot }, null,
            path => Directory.EnumerateDirectories(path), epicManifestDirectory: epicManifests);

        var automatic = detector.Detect();
        var savedSteam = detector.Detect(steamGameRoot + Path.DirectorySeparatorChar);
        var savedEpic = detector.Detect(epicGameRoot);

        Assert.True(automatic.IsAmbiguous);
        Assert.Null(automatic.GameRoot);
        Assert.False(automatic.IsMutationSupported);
        Assert.Equal(WwmInstallationSource.Steam, savedSteam.Source);
        Assert.Equal(Path.GetFullPath(steamGameRoot), savedSteam.GameRoot);
        Assert.Equal(Path.GetFullPath(steamGameRoot), savedSteam.PersistedGamePath);
        Assert.Equal(WwmInstallationSource.EpicGames, savedEpic.Source);
        Assert.Equal(Path.GetFullPath(epicGameRoot), savedEpic.GameRoot);
        Assert.Equal(Path.GetFullPath(epicGameRoot), savedEpic.PersistedGamePath);
        Assert.NotEqual(WwmGameDefinition.NormalizeInstallationRoot(steamGameRoot),
            WwmGameDefinition.NormalizeInstallationRoot(epicGameRoot));
    }

    [Fact]
    public void AutomaticSteamAndEpicEvidenceForEquivalentRoots_IsDeduplicated()
    {
        using var temp = new TestTemp();
        var (steamRoot, gameRoot) = CreateSteamRoot(temp.Root, "steam", "one-install");
        var manifests = Path.Combine(temp.Root, "epic-manifests");
        Directory.CreateDirectory(manifests);
        File.WriteAllText(Path.Combine(manifests, "wwm.item"), System.Text.Json.JsonSerializer.Serialize(
            new { AppName = "WhereWindsMeet", InstallLocation = gameRoot + Path.DirectorySeparatorChar }));
        var detector = new WwmSteamDetector(Logger, new[] { steamRoot }, null,
            path => Directory.EnumerateDirectories(path), epicManifestDirectory: manifests);

        var result = detector.Detect();

        Assert.False(result.IsAmbiguous);
        Assert.Equal(WwmInstallationSource.Steam, result.Source);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(gameRoot), result.GameRoot);
    }

    [Fact]
    public async Task SchemaV2TrailingSeparatorBinding_MatchesSameInstallationRoot()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "schema-v2-root");
        CreateGameMarkers(root);
        var detection = new WwmSteamDetector(Logger, Array.Empty<string>()).DescribeRoot(root, "manual-folder-selection");
        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(detection.TargetLayout);
        var installed = new[] { new byte[] { 31, 32 }, new byte[] { 41, 42 } };
        for (var index = 0; index < targets.Length; index++)
            await File.WriteAllBytesAsync(Path.Combine(root, targets[index]), installed[index]);
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var package = CreatePackageMetadata(CreatePackage());
        var state = CreateState(targets, installed);
        state.SchemaVersion = 2;
        state.ModeSlug = package.Mode.Slug;
        state.ModeVariant = package.Mode.Variant;
        state.ReleaseVersion = package.Version;
        state.ArchiveSizeBytes = package.SizeBytes;
        state.ArchiveSha256 = package.Sha256;
        state.InstallationRoot = root + Path.DirectorySeparatorChar;
        state.TargetLayout = detection.TargetLayout.ToString();
        state.InstallationFingerprint = WwmGameDefinition.ComputeInstallationFingerprint(root);
        await store.SaveAsync(state);
        using var client = new HttpClient(new StubHandler());
        var service = new WwmInstallService(client, paths, store, Logger);

        var resolved = await service.ResolveStateAsync(detection, package);

        Assert.Equal(WwmInstalledStateKind.Current, resolved.State);
    }

    [Fact]
    public async Task EpicPackageLayout_InstallsAndRestoresThroughProvenEpicBinding()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "unrelated-parent", "epic-install-7");
        CreateGameMarkers(root);
        var manifests = Path.Combine(temp.Root, "epic-manifests");
        Directory.CreateDirectory(manifests);
        File.WriteAllText(Path.Combine(manifests, "wwm.item"),
            System.Text.Json.JsonSerializer.Serialize(new { AppName = "WhereWindsMeet", InstallLocation = root }));
        var detection = new WwmSteamDetector(Logger, Array.Empty<string>(), null,
            path => Directory.EnumerateDirectories(path), epicManifestDirectory: manifests).Detect();
        Assert.Equal(WwmInstallationSource.EpicGames, detection.Source);
        Assert.Equal(WwmInstallationLayout.Package, detection.TargetLayout);
        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package);
        var originals = new[] { new byte[] { 71, 72 }, new byte[] { 81, 82 } };
        File.WriteAllBytes(Path.Combine(root, targets[0]), originals[0]);
        File.WriteAllBytes(Path.Combine(root, targets[1]), originals[1]);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger);

        Assert.True((await service.InstallAsync(detection, CreatePackageMetadata(archive))).IsSuccess);
        var basePackage = CreatePackageMetadata(archive);
        var updatePackage = new WwmApiPackage
        {
            Mode = basePackage.Mode, Version = "v-test-update", DownloadUrl = basePackage.DownloadUrl,
            SizeBytes = basePackage.SizeBytes, Sha256 = basePackage.Sha256
        };
        Assert.True((await service.InstallAsync(detection, updatePackage)).IsSuccess);
        Assert.Equal("v-test-update", store.Load(out _)!.ReleaseVersion);
        Assert.All(store.Load(out _)!.Targets, item => Assert.Equal(WwmInstallationLayout.Package, WwmStateStore.GetLayoutForPath(item.RelativePath)));
        Assert.True((await service.RestorePreHubAsync(detection)).IsSuccess);
        Assert.Equal(originals[0], await File.ReadAllBytesAsync(Path.Combine(root, targets[0])));
        Assert.Equal(originals[1], await File.ReadAllBytesAsync(Path.Combine(root, targets[1])));
    }

    [Fact]
    public async Task EpicPackagePartialFailure_RollsBackBothTargets()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "epic", "arbitrary-root");
        CreateGameMarkers(root);
        var manifests = Path.Combine(temp.Root, "epic-manifests");
        Directory.CreateDirectory(manifests);
        File.WriteAllText(Path.Combine(manifests, "wwm.item"),
            System.Text.Json.JsonSerializer.Serialize(new { AppName = "WhereWindsMeet", InstallLocation = root }));
        var detection = new WwmSteamDetector(Logger, Array.Empty<string>(), null,
            path => Directory.EnumerateDirectories(path), epicManifestDirectory: manifests).Detect();
        Assert.Equal(WwmInstallationSource.EpicGames, detection.Source);
        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package);
        var previous = new[] { new byte[] { 51, 52 }, new byte[] { 61, 62 } };
        for (var index = 0; index < targets.Length; index++) File.WriteAllBytes(Path.Combine(root, targets[index]), previous[index]);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger)
        {
            AfterTargetAppliedForTest = count => { if (count == 1) throw new IOException("Injected Epic target replacement failure."); }
        };

        var result = await service.InstallAsync(detection, CreatePackageMetadata(archive));

        Assert.Equal(WwmMutationError.Mutation, result.Error);
        for (var index = 0; index < targets.Length; index++)
            Assert.Equal(previous[index], await File.ReadAllBytesAsync(Path.Combine(root, targets[index])));
        Assert.Null(store.LoadJournal(out _));
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        AssertNoGameTemps(root);
    }

    [Fact]
    public async Task PackageInstall_ClearsReadOnlyOnLocalDataOverridesWithoutReplacingTheirBytes()
    {
        using var temp = new TestTemp();
        var (_, root) = CreateSteamRoot(temp.Root, "steam", "game");
        var localDataTargets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.LocalDataPatch);
        var localDataLocale = Path.Combine(root, WwmGameDefinition.Default.LocalDataPatchLocaleRelativePath);
        Directory.CreateDirectory(localDataLocale);
        var original = new[] { new byte[] { 0xA1, 0xA2 }, new byte[] { 0xB1, 0xB2, 0xB3 } };
        for (var index = 0; index < localDataTargets.Length; index++)
        {
            var path = Path.Combine(root, localDataTargets[index]);
            await File.WriteAllBytesAsync(path, original[index]);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }

        var detection = new WwmSteamDetector(Logger, new[] { Path.Combine(temp.Root, "steam") }).Detect();
        Assert.Equal(WwmInstallationLayout.Package, detection.TargetLayout);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var result = await new WwmInstallService(client, paths, store, Logger)
            .InstallAsync(detection, CreatePackageMetadata(archive));

        Assert.True(result.IsSuccess, result.Message);
        var packageTargets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package);
        Assert.All(packageTargets, relative => Assert.True(File.Exists(Path.Combine(root, relative))));
        for (var index = 0; index < localDataTargets.Length; index++)
        {
            var path = Path.Combine(root, localDataTargets[index]);
            Assert.Equal(original[index], await File.ReadAllBytesAsync(path));
            Assert.False((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
        }
        Assert.Equal(2, store.Load(out _)!.SchemaVersion);
        Assert.Null(store.LoadJournal(out _));
    }

    [Fact]
    public async Task PackageInstallFailure_RestoresLocalDataOverrideBytesAndReadOnlyAttributes()
    {
        using var temp = new TestTemp();
        var (_, root) = CreateSteamRoot(temp.Root, "steam", "game");
        var localDataTargets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.LocalDataPatch);
        Directory.CreateDirectory(Path.Combine(root, WwmGameDefinition.Default.LocalDataPatchLocaleRelativePath));
        var original = new[] { new byte[] { 0xC1, 0xC2 }, new byte[] { 0xD1, 0xD2 } };
        foreach (var (relative, index) in localDataTargets.Select((relative, index) => (relative, index)))
        {
            var path = Path.Combine(root, relative);
            await File.WriteAllBytesAsync(path, original[index]);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }

        var detection = new WwmSteamDetector(Logger, new[] { Path.Combine(temp.Root, "steam") }).Detect();
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger)
        {
            AfterTargetAppliedForTest = count => { if (count == 1) throw new IOException("Injected target failure after LocalData attributes changed."); }
        };

        var result = await service.InstallAsync(detection, CreatePackageMetadata(archive));

        Assert.Equal(WwmMutationError.Mutation, result.Error);
        for (var index = 0; index < localDataTargets.Length; index++)
        {
            var path = Path.Combine(root, localDataTargets[index]);
            Assert.Equal(original[index], await File.ReadAllBytesAsync(path));
            Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
        }
        Assert.Null(store.Load(out _));
        Assert.Null(store.LoadJournal(out _));
        AssertNoGameTemps(root);
    }

    [Fact]
    public async Task PackageInstallCrashRecovery_RestoresLocalDataReadOnlyWithoutRewritingItsBytes()
    {
        using var temp = new TestTemp();
        var (_, root) = CreateSteamRoot(temp.Root, "steam", "game");
        var detection = new WwmSteamDetector(Logger, new[] { Path.Combine(temp.Root, "steam") }).Detect();
        var packageTargets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package);
        var localDataTargets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.LocalDataPatch);
        Directory.CreateDirectory(Path.Combine(root, WwmGameDefinition.Default.LocalDataPatchLocaleRelativePath));
        var localDataBytes = new[] { new byte[] { 0xE1, 0xE2 }, new byte[] { 0xF1, 0xF2 } };
        var packageBytes = new[] { new byte[] { 0x31, 0x32 }, new byte[] { 0x41, 0x42 } };
        var attributeRecords = new List<WwmPreviousTarget>();
        var previousTargets = new List<WwmPreviousTarget>();
        var expectedTargets = new List<WwmTargetManifestEntry>();
        for (var index = 0; index < 2; index++)
        {
            var localPath = Path.Combine(root, localDataTargets[index]);
            await File.WriteAllBytesAsync(localPath, localDataBytes[index]);
            var originalAttributes = File.GetAttributes(localPath) | FileAttributes.ReadOnly;
            File.SetAttributes(localPath, originalAttributes);
            attributeRecords.Add(new WwmPreviousTarget
            {
                RelativePath = localDataTargets[index], Existed = true, SizeBytes = localDataBytes[index].Length,
                Sha256 = Hash(localDataBytes[index]), Attributes = (int)originalAttributes
            });
            File.SetAttributes(localPath, originalAttributes & ~FileAttributes.ReadOnly);

            previousTargets.Add(new WwmPreviousTarget { RelativePath = packageTargets[index], Existed = false });
            expectedTargets.Add(new WwmTargetManifestEntry
            {
                RelativePath = packageTargets[index], SizeBytes = packageBytes[index].Length, Sha256 = Hash(packageBytes[index])
            });
        }
        await File.WriteAllBytesAsync(Path.Combine(root, packageTargets[0]), packageBytes[0]);

        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var operationId = Guid.NewGuid().ToString("N");
        var binding = WwmInstallationBinding.FromDetection(detection);
        await store.WriteJournalAsync(new WwmTransactionJournal
        {
            SchemaVersion = 3, OperationId = operationId, Operation = "install", PreviousStateExisted = false,
            PreviousStateBase64 = "", PreviousTargets = previousTargets, AttributeOnlyTargets = attributeRecords,
            TargetManifest = expectedTargets, ExpectedAbsentTargets = new(), ExpectedStateSha256 = Hash(new byte[] { 0x5A }),
            InstallationRoot = Path.GetFullPath(root), TargetLayout = WwmInstallationLayout.Package.ToString(),
            InstallationFingerprint = binding.Fingerprint
        });

        var recovered = await new WwmInstallService(new HttpClient(new StubHandler()), paths, store, Logger).RecoverAsync(detection);

        Assert.True(recovered.IsSuccess, recovered.Message);
        Assert.False(File.Exists(Path.Combine(root, packageTargets[0])));
        for (var index = 0; index < 2; index++)
        {
            var localPath = Path.Combine(root, localDataTargets[index]);
            Assert.Equal(localDataBytes[index], await File.ReadAllBytesAsync(localPath));
            Assert.True((File.GetAttributes(localPath) & FileAttributes.ReadOnly) != 0);
        }
        Assert.Null(store.LoadJournal(out _));
        AssertNoGameTemps(root);
    }

    [Fact]
    public async Task EpicPackageCrashRecovery_RestoresJournaledPreviousFiles()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "epic", "not-a-known-folder");
        CreateGameMarkers(root);
        var manifests = Path.Combine(temp.Root, "epic-manifests");
        Directory.CreateDirectory(manifests);
        File.WriteAllText(Path.Combine(manifests, "wwm.item"),
            System.Text.Json.JsonSerializer.Serialize(new { ArtifactId = "where-winds-meet", InstallLocation = root }));
        var detection = new WwmSteamDetector(Logger, Array.Empty<string>(), null,
            path => Directory.EnumerateDirectories(path), epicManifestDirectory: manifests).Detect();
        Assert.Equal(WwmInstallationSource.EpicGames, detection.Source);
        var targets = detection.TargetLayout == WwmInstallationLayout.Package
            ? WwmGameDefinition.Default.GetManagedRelativePaths(detection.TargetLayout)
            : throw new InvalidOperationException("Expected Package target layout.");
        var previous = new[] { new byte[] { 71, 72 }, new byte[] { 81, 82 } };
        var installed = new[] { new byte[] { 31, 32 }, new byte[] { 41, 42 } };
        for (var index = 0; index < targets.Length; index++) File.WriteAllBytes(Path.Combine(root, targets[index]), previous[index]);
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var operationId = Guid.NewGuid().ToString("N");
        var operationDirectory = Path.Combine(store.TransactionDirectory, operationId);
        Directory.CreateDirectory(operationDirectory);
        var previousRecords = new List<WwmPreviousTarget>();
        var expected = new List<WwmTargetManifestEntry>();
        for (var index = 0; index < targets.Length; index++)
        {
            await File.WriteAllBytesAsync(Path.Combine(operationDirectory, index + ".bin"), previous[index]);
            previousRecords.Add(new WwmPreviousTarget { RelativePath = targets[index], Existed = true,
                SizeBytes = previous[index].Length, Sha256 = Hash(previous[index]), BackupFile = index + ".bin" });
            expected.Add(new WwmTargetManifestEntry { RelativePath = targets[index], SizeBytes = installed[index].Length,
                Sha256 = Hash(installed[index]) });
        }
        File.WriteAllBytes(Path.Combine(root, targets[0]), installed[0]);
        var binding = WwmInstallationBinding.FromDetection(detection);
        await store.WriteJournalAsync(new WwmTransactionJournal
        {
            SchemaVersion = 2, OperationId = operationId, Operation = "install", PreviousTargets = previousRecords,
            TargetManifest = expected, ExpectedAbsentTargets = new(), ExpectedStateSha256 = Hash(new byte[] { 2 }),
            PreviousStateExisted = false, PreviousStateBase64 = "", InstallationRoot = Path.GetFullPath(root),
            TargetLayout = detection.TargetLayout.ToString(), InstallationFingerprint = binding.Fingerprint
        });
        using var client = new HttpClient(new StubHandler());
        var service = new WwmInstallService(client, paths, store, Logger);

        var recovery = await service.RecoverAsync(detection);

        Assert.True(recovery.IsSuccess, recovery.Message);
        for (var index = 0; index < targets.Length; index++)
            Assert.Equal(previous[index], await File.ReadAllBytesAsync(Path.Combine(root, targets[index])));
        Assert.Null(store.LoadJournal(out _));
    }

    [Fact]
    public async Task LegacyV1SteamPackageStateAndSnapshot_RemainReadableAndRestorable()
    {
        using var temp = new TestTemp();
        var steamRoot = Path.Combine(temp.Root, "steam");
        var library = steamRoot;
        var root = Path.Combine(library, "steamapps", "common", "arbitrary-game-folder");
        CreateGameMarkers(root);
        var manifestPath = Path.Combine(library, "steamapps", "appmanifest_3564740.acf");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, "\"appid\" \"3564740\" \"installdir\" \"arbitrary-game-folder\" \"buildid\" \"25281753\"");
        var detection = new WwmSteamDetector(Logger, new[] { steamRoot }).Detect(root + Path.DirectorySeparatorChar);
        detection = detection with { PersistedGamePath = root + Path.DirectorySeparatorChar };
        Assert.Equal(WwmInstallationSource.Steam, detection.Source);

        var archive = CreatePackage();
        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package);
        var installed = new[] { new byte[] { 10, 11 }, new byte[] { 12, 13 } };
        for (var index = 0; index < targets.Length; index++)
            await File.WriteAllBytesAsync(Path.Combine(root, targets[index]), installed[index]);
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var cycleId = Guid.NewGuid().ToString("N");
        var package = CreatePackageMetadata(archive);
        var legacyState = new WwmInstallationState
        {
            SchemaVersion = 1, ModeSlug = package.Mode.Slug, ModeVariant = package.Mode.Variant,
            ReleaseVersion = package.Version, ArchiveSizeBytes = package.SizeBytes, ArchiveSha256 = package.Sha256,
            PreHubSnapshotCycleId = cycleId, GameBuildId = detection.BuildId, InstalledAt = DateTimeOffset.UtcNow,
            Targets = installed.Select((bytes, index) => new WwmTargetManifestEntry
                { RelativePath = targets[index], SizeBytes = bytes.Length, Sha256 = Hash(bytes) }).ToList()
        };
        await store.SaveAsync(legacyState);
        var previous = new[] { new byte[] { 90, 91 }, new byte[] { 92, 93, 94 } };
        await CreatePreHubSnapshotAsync(store, targets, previous, detection.BuildId!, cycleId);
        var client = new HttpClient(new StubHandler());
        var service = new WwmInstallService(client, paths, store, Logger);

        var legacyLoaded = store.Load(out var stateError);
        Assert.Equal(1, legacyLoaded!.SchemaVersion);
        var current = await service.ResolveStateAsync(detection, package);
        var migrated = store.Load(out _);
        var migratedSnapshot = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(store.SnapshotDirectory, "snapshot.json")));
        var restoreAvailable = await service.CheckRestoreAvailabilityAsync(detection);
        var restored = await service.RestorePreHubAsync(detection);

        Assert.Null(stateError);
        Assert.Equal(2, migrated!.SchemaVersion);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(root), migrated.InstallationRoot);
        Assert.Equal("Package", migrated.TargetLayout);
        Assert.Equal(2, migratedSnapshot.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(root), migratedSnapshot.RootElement.GetProperty("installation_root").GetString());
        Assert.Equal(WwmInstalledStateKind.Current, current.State);
        Assert.True(restoreAvailable.IsAvailable, restoreAvailable.Message);
        Assert.True(restored.IsSuccess, restored.Message);
        Assert.Equal(previous[0], await File.ReadAllBytesAsync(Path.Combine(root, targets[0])));
        Assert.Equal(previous[1], await File.ReadAllBytesAsync(Path.Combine(root, targets[1])));
    }

    [Fact]
    public async Task V1210AutoDetectedSteamLegacyCycle_MigratesAndPersistsRootWithoutChangingGameFiles()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync();
        var detection = fixture.Session.DetectGame();
        Assert.Equal(WwmInstallationSource.Steam, detection.Source);
        Assert.True(detection.IsUniqueAutomaticSteamDetection);
        Assert.Null(detection.PersistedGamePath);
        var filesBefore = await ReadTargetsAsync(fixture.Root, fixture.Targets);

        var recovery = await fixture.Session.InstallService.RecoverAsync(detection);
        var state = await fixture.Session.InstallService.ResolveStateAsync(detection, fixture.Package);
        var restoreAvailable = await fixture.Session.InstallService.CheckRestoreAvailabilityAsync(detection);

        Assert.True(recovery.IsSuccess, recovery.Message);
        Assert.Equal(WwmInstalledStateKind.Current, state.State);
        Assert.True(restoreAvailable.IsAvailable, restoreAvailable.Message);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), fixture.Session.ConfigStore.Load().Value!.GamePath);
        var migrated = fixture.Session.StateStore.Load(out var stateError);
        Assert.Null(stateError);
        Assert.Equal(2, migrated!.SchemaVersion);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), migrated.InstallationRoot);
        Assert.Equal(nameof(WwmInstallationLayout.Package), migrated.TargetLayout);
        Assert.Equal(fixture.Targets, migrated.Targets.Select(item => item.RelativePath).ToArray());
        var snapshotJson = await File.ReadAllTextAsync(Path.Combine(fixture.Session.StateStore.SnapshotDirectory, "snapshot.json"));
        var snapshot = System.Text.Json.JsonDocument.Parse(snapshotJson).RootElement;
        Assert.Equal(2, snapshot.GetProperty("schema_version").GetInt32());
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), snapshot.GetProperty("installation_root").GetString());
        var filesAfter = await ReadTargetsAsync(fixture.Root, fixture.Targets);
        for (var index = 0; index < fixture.Targets.Length; index++)
            Assert.Equal(filesBefore[index], filesAfter[index]);
    }

    [Fact]
    public async Task V1210LegacyCycle_AmbiguousAutoDetectionThenManualSteamSelectionMigratesInSameSession()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync(addSecondSteamRoot: true);
        var automatic = fixture.Session.DetectGame();
        Assert.True(automatic.IsAmbiguous);
        Assert.Null(automatic.GameRoot);
        Assert.Null(automatic.PersistedGamePath);
        var filesBefore = await ReadTargetsAsync(fixture.Root, fixture.Targets);

        var manual = await fixture.Session.ResolveAndSaveManualPathAsync(fixture.Root);

        Assert.Equal(ManualResolveStatus.Found, manual.Status);
        Assert.NotNull(manual.Detection);
        Assert.Equal(WwmInstallationSource.Steam, manual.Detection!.Source);
        Assert.False(manual.Detection.IsUniqueAutomaticSteamDetection);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), manual.Detection.PersistedGamePath);
        Assert.Same(manual.Detection, fixture.Session.CurrentDetection);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), fixture.Session.ConfigStore.Load().Value!.GamePath);

        var recovery = await fixture.Session.InstallService.RecoverAsync(fixture.Session.CurrentDetection);
        var state = await fixture.Session.InstallService.ResolveStateAsync(fixture.Session.CurrentDetection, fixture.Package);
        var restoreAvailability = await fixture.Session.InstallService.CheckRestoreAvailabilityAsync(fixture.Session.CurrentDetection);
        var migrated = fixture.Session.StateStore.Load(out var stateError);
        var filesAfter = await ReadTargetsAsync(fixture.Root, fixture.Targets);

        Assert.True(recovery.IsSuccess, recovery.Message);
        Assert.Equal(WwmInstalledStateKind.Current, state.State);
        Assert.True(restoreAvailability.IsAvailable, restoreAvailability.Message);
        Assert.Null(stateError);
        Assert.Equal(2, migrated!.SchemaVersion);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), migrated.InstallationRoot);
        for (var index = 0; index < fixture.Targets.Length; index++)
            Assert.Equal(filesBefore[index], filesAfter[index]);
    }

    [Fact]
    public async Task ManualSteamSelection_ConfigSaveFailureDoesNotGrantPersistedRootEvidence()
    {
        using var temp = new TestTemp();
        var (steamRoot, gameRoot) = CreateSteamRoot(temp.Root, "steam-manual-save-failure", "game");
        using var client = new HttpClient(new StubHandler());
        using var session = WwmGameSession.CreateForTests(new AppPaths(Path.Combine(temp.Root, "app")), Logger, client,
            steamRoots: new[] { steamRoot });
        var priorDetection = session.CurrentDetection;
        Directory.CreateDirectory(session.PersistencePaths.ConfigFile);

        await Assert.ThrowsAnyAsync<IOException>(() => session.ResolveAndSaveManualPathAsync(gameRoot));

        Assert.Same(priorDetection, session.CurrentDetection);
        Assert.Null(session.CurrentDetection.PersistedGamePath);
        Assert.Null(session.ConfigStore.Load().Value?.GamePath);
    }

    [Fact]
    public async Task ManualUnknownWindowsSelection_DoesNotBindLegacySteamCycle()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync();
        var unknownRoot = Path.Combine(Path.GetDirectoryName(fixture.Root)!, "user-selected-unknown-root");
        CreateGameMarkers(unknownRoot);
        var originalBytes = await ReadTargetsAsync(fixture.Root, fixture.Targets);

        var manual = await fixture.Session.ResolveAndSaveManualPathAsync(unknownRoot);
        Assert.Equal(ManualResolveStatus.Found, manual.Status);
        Assert.Equal(WwmInstallationSource.UnknownWindows, manual.Detection!.Source);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(unknownRoot), manual.Detection.PersistedGamePath);

        var recovery = await fixture.Session.InstallService.RecoverAsync(fixture.Session.CurrentDetection);
        var state = fixture.Session.StateStore.Load(out _);
        var filesAfter = await ReadTargetsAsync(fixture.Root, fixture.Targets);

        Assert.False(recovery.IsSuccess);
        Assert.Equal(WwmMutationError.RecoveryRequired, recovery.Error);
        Assert.Equal(1, state!.SchemaVersion);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(unknownRoot), fixture.Session.ConfigStore.Load().Value!.GamePath);
        for (var index = 0; index < fixture.Targets.Length; index++)
            Assert.Equal(originalBytes[index], filesAfter[index]);
    }

    [Fact]
    public async Task V1210AutoDetectedSteamLegacyCycle_WithChangedTarget_FailsClosedWithoutBindingOrMutation()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync();
        var detection = fixture.Session.DetectGame();
        var changed = new byte[] { 201, 202, 203 };
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, fixture.Targets[0]), changed);
        var unchangedSecond = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, fixture.Targets[1]));

        var recovery = await fixture.Session.InstallService.RecoverAsync(detection);
        var state = fixture.Session.StateStore.Load(out _);
        var snapshotJson = await File.ReadAllTextAsync(Path.Combine(fixture.Session.StateStore.SnapshotDirectory, "snapshot.json"));

        Assert.False(recovery.IsSuccess);
        Assert.Equal(WwmMutationError.RecoveryRequired, recovery.Error);
        Assert.Null(fixture.Session.ConfigStore.Load().Value?.GamePath);
        Assert.Equal(1, state!.SchemaVersion);
        Assert.Equal(1, System.Text.Json.JsonDocument.Parse(snapshotJson).RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal(changed, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, fixture.Targets[0])));
        Assert.Equal(unchangedSecond, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, fixture.Targets[1])));
    }

    [Fact]
    public async Task V1210AutoDetectedSteamLegacyCycle_WithConflictingCycle_FailsClosed()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync();
        var detection = fixture.Session.DetectGame();
        var legacy = fixture.Session.StateStore.Load(out _)!;
        legacy.PreHubSnapshotCycleId = Guid.NewGuid().ToString("N");
        await fixture.Session.StateStore.SaveAsync(legacy);
        var filesBefore = await ReadTargetsAsync(fixture.Root, fixture.Targets);

        var recovery = await fixture.Session.InstallService.RecoverAsync(detection);

        Assert.False(recovery.IsSuccess);
        Assert.Equal(WwmMutationError.RecoveryRequired, recovery.Error);
        Assert.Null(fixture.Session.ConfigStore.Load().Value?.GamePath);
        Assert.Equal(1, fixture.Session.StateStore.Load(out _)!.SchemaVersion);
        var filesAfter = await ReadTargetsAsync(fixture.Root, fixture.Targets);
        for (var index = 0; index < fixture.Targets.Length; index++)
            Assert.Equal(filesBefore[index], filesAfter[index]);
    }

    [Fact]
    public async Task V1210LegacySteamCycle_AutoDetectedAfterGameUpdateMigratesButRestoreRemainsStale()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync(currentBuildId: "25906190", stateBuildId: "25281754");
        var detection = fixture.Session.DetectGame();
        Assert.Equal("25906190", detection.BuildId);
        Assert.Null(detection.PersistedGamePath);
        var filesBefore = await ReadTargetsAsync(fixture.Root, fixture.Targets);

        var recovery = await fixture.Session.InstallService.RecoverAsync(detection);

        Assert.True(recovery.IsSuccess, recovery.Message);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), fixture.Session.ConfigStore.Load().Value!.GamePath);
        var migratedState = fixture.Session.StateStore.Load(out var stateError);
        Assert.Null(stateError);
        Assert.Equal(2, migratedState!.SchemaVersion);
        Assert.Equal("25281754", migratedState.GameBuildId);
        var snapshotJson = await File.ReadAllTextAsync(Path.Combine(fixture.Session.StateStore.SnapshotDirectory, "snapshot.json"));
        using (var snapshotDocument = System.Text.Json.JsonDocument.Parse(snapshotJson))
        {
            var snapshot = snapshotDocument.RootElement;
            Assert.Equal(2, snapshot.GetProperty("schema_version").GetInt32());
            Assert.Equal("25281753", snapshot.GetProperty("game_build_id").GetString());
            Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), snapshot.GetProperty("installation_root").GetString());
        }

        var state = await fixture.Session.InstallService.ResolveStateAsync(detection, fixture.Package);
        var restoreAvailability = await fixture.Session.InstallService.CheckRestoreAvailabilityAsync(detection);
        Assert.Equal(WwmInstalledStateKind.Current, state.State);
        Assert.False(restoreAvailability.IsAvailable);
        Assert.Contains("Steam build відрізняється", restoreAvailability.Message, StringComparison.Ordinal);
        var restore = await fixture.Session.InstallService.RestorePreHubAsync(detection);
        Assert.Equal(WwmMutationError.SnapshotStale, restore.Error);
        Assert.Contains("Steam build відрізняється", restore.Message, StringComparison.Ordinal);
        var updatePreparation = await fixture.Session.InstallService.InstallAsync(detection, fixture.Package);
        Assert.Equal(WwmMutationError.Package, updatePreparation.Error);
        Assert.DoesNotContain("Steam build відрізняється", updatePreparation.Message, StringComparison.Ordinal);
        var filesAfterMigration = await ReadTargetsAsync(fixture.Root, fixture.Targets);
        for (var index = 0; index < filesBefore.Length; index++)
            Assert.Equal(filesBefore[index], filesAfterMigration[index]);

        // Model a cold process using persisted config and migrated on-disk metadata.
        using var restarted = WwmGameSession.CreateForTests(
            new AppPaths(fixture.Session.PersistencePaths.ApplicationRoot), Logger,
            new HttpClient(new StubHandler()), ownsHttpClient: true,
            steamRoots: new[] { Path.GetFullPath(Path.Combine(fixture.Root, "..", "..", "..")) });
        var restartedDetection = restarted.DetectGame();
        Assert.Equal("25906190", restartedDetection.BuildId);
        Assert.Equal(WwmInstallationSource.Steam, restartedDetection.Source);
        Assert.True((await restarted.InstallService.RecoverAsync(restartedDetection)).IsSuccess);
        Assert.Equal(WwmInstalledStateKind.Current,
            (await restarted.InstallService.ResolveStateAsync(restartedDetection, fixture.Package)).State);
        var restartedRestore = await restarted.InstallService.CheckRestoreAvailabilityAsync(restartedDetection);
        Assert.False(restartedRestore.IsAvailable);
        Assert.Contains("Steam build відрізняється", restartedRestore.Message, StringComparison.Ordinal);
        var filesAfterRestart = await ReadTargetsAsync(fixture.Root, fixture.Targets);
        for (var index = 0; index < filesBefore.Length; index++)
            Assert.Equal(filesBefore[index], filesAfterRestart[index]);
    }

    [Fact]
    public async Task V1210LegacySteamCycle_ManualSameSessionMigrationAllowsHistoricalBuildAndKeepsRestoreStale()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync(addSecondSteamRoot: true, currentBuildId: "25906190");
        var filesBefore = await ReadTargetsAsync(fixture.Root, fixture.Targets);

        var manual = await fixture.Session.ResolveAndSaveManualPathAsync(fixture.Root);
        var recovery = await fixture.Session.InstallService.RecoverAsync(manual.Detection!);
        var restoreAvailability = await fixture.Session.InstallService.CheckRestoreAvailabilityAsync(manual.Detection!);

        Assert.True(recovery.IsSuccess, recovery.Message);
        Assert.Equal(2, fixture.Session.StateStore.Load(out _)!.SchemaVersion);
        Assert.False(restoreAvailability.IsAvailable);
        Assert.Contains("Steam build відрізняється", restoreAvailability.Message, StringComparison.Ordinal);
        var filesAfter = await ReadTargetsAsync(fixture.Root, fixture.Targets);
        for (var index = 0; index < filesBefore.Length; index++)
            Assert.Equal(filesBefore[index], filesAfter[index]);
    }

    [Fact]
    public async Task V1210IncompleteLegacyJournal_AfterSteamBuildChangeFailsClosedWithoutRollback()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync(currentBuildId: "25906190");
        var detection = fixture.Session.DetectGame();
        var store = fixture.Session.StateStore;
        var stateBytes = await File.ReadAllBytesAsync(store.StateFile);
        var operationId = Guid.NewGuid().ToString("N");
        var operationDirectory = Path.Combine(store.TransactionDirectory, operationId);
        Directory.CreateDirectory(operationDirectory);
        var previousTargets = new List<WwmPreviousTarget>();
        var targetManifest = new List<WwmTargetManifestEntry>();
        for (var index = 0; index < fixture.Targets.Length; index++)
        {
            var current = fixture.Installed[index];
            await File.WriteAllBytesAsync(Path.Combine(operationDirectory, index + ".bin"), current);
            previousTargets.Add(new WwmPreviousTarget
            {
                RelativePath = fixture.Targets[index], Existed = true, SizeBytes = current.Length,
                Sha256 = Hash(current), BackupFile = index + ".bin"
            });
            var expected = new byte[] { (byte)(180 + index), (byte)(190 + index) };
            targetManifest.Add(new WwmTargetManifestEntry
            {
                RelativePath = fixture.Targets[index], SizeBytes = expected.Length, Sha256 = Hash(expected)
            });
        }
        await store.WriteJournalAsync(new WwmTransactionJournal
        {
            SchemaVersion = 1, OperationId = operationId, Operation = "install", PreviousTargets = previousTargets,
            TargetManifest = targetManifest, ExpectedAbsentTargets = new(), ExpectedStateSha256 = Hash(new byte[] { 77 }),
            PreviousStateExisted = true, PreviousStateBase64 = Convert.ToBase64String(stateBytes), GameBuildId = "25281753"
        });
        var filesBefore = await ReadTargetsAsync(fixture.Root, fixture.Targets);

        var recovery = await fixture.Session.InstallService.RecoverAsync(detection);

        Assert.False(recovery.IsSuccess);
        Assert.Equal(WwmMutationError.SnapshotStale, recovery.Error);
        Assert.Contains("Steam build відрізняється", recovery.Message, StringComparison.Ordinal);
        Assert.NotNull(store.LoadJournal(out _));
        var filesAfter = await ReadTargetsAsync(fixture.Root, fixture.Targets);
        for (var index = 0; index < filesBefore.Length; index++)
            Assert.Equal(filesBefore[index], filesAfter[index]);
    }

    [Fact]
    public async Task V1210AutoDetectedLegacyCycle_WithMultipleSteamRoots_RemainsAmbiguousAndUnbound()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync(addSecondSteamRoot: true);

        var detection = fixture.Session.DetectGame();

        Assert.True(detection.IsAmbiguous);
        Assert.Null(detection.GameRoot);
        Assert.False(detection.IsMutationSupported);
        Assert.Null(fixture.Session.ConfigStore.Load().Value?.GamePath);
        Assert.Equal(1, fixture.Session.StateStore.Load(out _)!.SchemaVersion);
    }

    [Fact]
    public async Task V1210AutoDetectedLegacyJournal_BindsOnlyAfterKnownTargetAndCycleValidation()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync();
        var detection = fixture.Session.DetectGame();
        var store = fixture.Session.StateStore;
        var stateBytes = await File.ReadAllBytesAsync(store.StateFile);
        var operationId = Guid.NewGuid().ToString("N");
        var operationDirectory = Path.Combine(store.TransactionDirectory, operationId);
        Directory.CreateDirectory(operationDirectory);
        var previousTargets = new List<WwmPreviousTarget>();
        var expectedTargets = new List<WwmTargetManifestEntry>();
        for (var index = 0; index < fixture.Targets.Length; index++)
        {
            var bytes = fixture.Installed[index];
            await File.WriteAllBytesAsync(Path.Combine(operationDirectory, index + ".bin"), bytes);
            previousTargets.Add(new WwmPreviousTarget
            {
                RelativePath = fixture.Targets[index], Existed = true, SizeBytes = bytes.Length,
                Sha256 = Hash(bytes), BackupFile = index + ".bin"
            });
            var future = new byte[] { (byte)(150 + index), (byte)(160 + index) };
            expectedTargets.Add(new WwmTargetManifestEntry
            {
                RelativePath = fixture.Targets[index], SizeBytes = future.Length, Sha256 = Hash(future)
            });
        }
        await store.WriteJournalAsync(new WwmTransactionJournal
        {
            SchemaVersion = 1, OperationId = operationId, Operation = "install", PreviousTargets = previousTargets,
            TargetManifest = expectedTargets, ExpectedAbsentTargets = new(), ExpectedStateSha256 = Hash(new byte[] { 99 }),
            PreviousStateExisted = true, PreviousStateBase64 = Convert.ToBase64String(stateBytes), GameBuildId = detection.BuildId
        });

        var recovered = await fixture.Session.InstallService.RecoverAsync(detection);

        Assert.True(recovered.IsSuccess, recovered.Message);
        Assert.Null(store.LoadJournal(out _));
        Assert.Equal(2, store.Load(out _)!.SchemaVersion);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), fixture.Session.ConfigStore.Load().Value!.GamePath);
        var filesAfter = await ReadTargetsAsync(fixture.Root, fixture.Targets);
        for (var index = 0; index < fixture.Targets.Length; index++)
            Assert.Equal(fixture.Installed[index], filesAfter[index]);
    }

    [Fact]
    public async Task V1210AutoDetectedCommittedLegacyInstallJournal_MigratesAndCleansWithoutRollback()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync(currentBuildId: "25906190");
        var detection = fixture.Session.DetectGame();
        var store = fixture.Session.StateStore;
        var committedStateBytes = await File.ReadAllBytesAsync(store.StateFile);
        var committedState = store.Load(out _)!;
        var activeCycleId = committedState.PreHubSnapshotCycleId;
        var previousTargetsBytes = new[] { new byte[] { 51, 52, 53 }, new byte[] { 61, 62, 63 } };
        var previousState = System.Text.Json.JsonSerializer.Deserialize<WwmInstallationState>(
            System.Text.Json.JsonSerializer.Serialize(committedState))!;
        previousState.ReleaseVersion = "v-old";
        previousState.Targets = previousTargetsBytes.Select((bytes, index) => new WwmTargetManifestEntry
        {
            RelativePath = fixture.Targets[index], SizeBytes = bytes.Length, Sha256 = Hash(bytes)
        }).ToList();

        var operationId = Guid.NewGuid().ToString("N");
        var operationDirectory = Path.Combine(store.TransactionDirectory, operationId);
        Directory.CreateDirectory(operationDirectory);
        var previousTargets = new List<WwmPreviousTarget>();
        for (var index = 0; index < fixture.Targets.Length; index++)
        {
            await File.WriteAllBytesAsync(Path.Combine(operationDirectory, index + ".bin"), previousTargetsBytes[index]);
            previousTargets.Add(new WwmPreviousTarget
            {
                RelativePath = fixture.Targets[index], Existed = true, SizeBytes = previousTargetsBytes[index].Length,
                Sha256 = Hash(previousTargetsBytes[index]), BackupFile = index + ".bin"
            });
        }
        await store.WriteJournalAsync(new WwmTransactionJournal
        {
            SchemaVersion = 1, OperationId = operationId, Operation = "install",
            PreviousTargets = previousTargets,
            TargetManifest = fixture.Installed.Select((bytes, index) => new WwmTargetManifestEntry
            {
                RelativePath = fixture.Targets[index], SizeBytes = bytes.Length, Sha256 = Hash(bytes)
            }).ToList(),
            ExpectedAbsentTargets = new(), ExpectedStateSha256 = Hash(committedStateBytes),
            PreviousStateExisted = true,
            PreviousStateBase64 = Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(previousState)),
            GameBuildId = "25281753"
        });

        var recovered = await fixture.Session.InstallService.RecoverAsync(detection);

        Assert.True(recovered.IsSuccess, recovered.Message);
        Assert.Null(store.LoadJournal(out _));
        Assert.False(Directory.Exists(operationDirectory));
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), fixture.Session.ConfigStore.Load().Value!.GamePath);
        var migratedState = store.Load(out var stateError);
        Assert.Null(stateError);
        Assert.Equal(2, migratedState!.SchemaVersion);
        Assert.Equal(committedState.ReleaseVersion, migratedState.ReleaseVersion);
        Assert.Equal(activeCycleId, migratedState.PreHubSnapshotCycleId);
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), migratedState.InstallationRoot);
        var snapshot = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(store.SnapshotDirectory, "snapshot.json"))).RootElement;
        Assert.Equal(2, snapshot.GetProperty("schema_version").GetInt32());
        Assert.Equal(activeCycleId, snapshot.GetProperty("cycle_operation_id").GetString());
        var targetsAfter = await ReadTargetsAsync(fixture.Root, fixture.Targets);
        for (var index = 0; index < fixture.Targets.Length; index++)
            Assert.Equal(fixture.Installed[index], targetsAfter[index]);
    }

    [Fact]
    public async Task V1210AutoDetectedCommittedLegacyRestoreJournal_KeepsAbsentStateAndRetiresSnapshot()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync(currentBuildId: "25906190");
        var detection = fixture.Session.DetectGame();
        var store = fixture.Session.StateStore;
        var previousState = store.Load(out _)!;
        var previousStateBytes = await File.ReadAllBytesAsync(store.StateFile);
        var restoredTargets = new[]
        {
            await File.ReadAllBytesAsync(Path.Combine(store.SnapshotDirectory, "0.bin")),
            await File.ReadAllBytesAsync(Path.Combine(store.SnapshotDirectory, "1.bin"))
        };
        var cycleId = previousState.PreHubSnapshotCycleId;
        var operationId = Guid.NewGuid().ToString("N");
        var operationDirectory = Path.Combine(store.TransactionDirectory, operationId);
        Directory.CreateDirectory(operationDirectory);
        var previousTargets = new List<WwmPreviousTarget>();
        for (var index = 0; index < fixture.Targets.Length; index++)
        {
            var bytes = fixture.Installed[index];
            await File.WriteAllBytesAsync(Path.Combine(operationDirectory, index + ".bin"), bytes);
            previousTargets.Add(new WwmPreviousTarget
            {
                RelativePath = fixture.Targets[index], Existed = true, SizeBytes = bytes.Length,
                Sha256 = Hash(bytes), BackupFile = index + ".bin"
            });
            await File.WriteAllBytesAsync(Path.Combine(fixture.Root, fixture.Targets[index]), restoredTargets[index]);
        }
        await store.ClearAsync();
        await store.WriteJournalAsync(new WwmTransactionJournal
        {
            SchemaVersion = 1, OperationId = operationId, Operation = "restore", PreviousTargets = previousTargets,
            TargetManifest = restoredTargets.Select((bytes, index) => new WwmTargetManifestEntry
            {
                RelativePath = fixture.Targets[index], SizeBytes = bytes.Length, Sha256 = Hash(bytes)
            }).ToList(),
            ExpectedAbsentTargets = new(), ExpectedStateSha256 = "absent", PreviousStateExisted = true,
            PreviousStateBase64 = Convert.ToBase64String(previousStateBytes), GameBuildId = "25281753"
        });

        var recovered = await fixture.Session.InstallService.RecoverAsync(detection);

        Assert.True(recovered.IsSuccess, recovered.Message);
        Assert.Null(store.LoadJournal(out _));
        Assert.False(File.Exists(store.StateFile));
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        Assert.False(Directory.Exists(operationDirectory));
        Assert.Equal(WwmGameDefinition.NormalizeInstallationRoot(fixture.Root), fixture.Session.ConfigStore.Load().Value!.GamePath);
        Assert.Equal(cycleId, previousState.PreHubSnapshotCycleId);
        var targetsAfter = await ReadTargetsAsync(fixture.Root, fixture.Targets);
        for (var index = 0; index < fixture.Targets.Length; index++)
            Assert.Equal(restoredTargets[index], targetsAfter[index]);
    }

    [Fact]
    public async Task V1210AutoDetectedLegacyJournal_WithUnlinkedPreviousStateAndOrphanCycleDirectory_FailsClosed()
    {
        using var fixture = await CreateLegacyAutoDetectedFixtureAsync();
        var detection = fixture.Session.DetectGame();
        var store = fixture.Session.StateStore;
        var currentState = store.Load(out _)!;
        var activeCycleId = currentState.PreHubSnapshotCycleId;
        var filesBefore = await ReadTargetsAsync(fixture.Root, fixture.Targets);

        // This directory is generic snapshot-cycle evidence, not proof that this journal owns that cycle.
        Directory.CreateDirectory(Path.Combine(store.TransactionDirectory, activeCycleId));
        var unrelatedPreviousState = currentState;
        unrelatedPreviousState.PreHubSnapshotCycleId = Guid.NewGuid().ToString("N");
        var previousStateBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(unrelatedPreviousState);
        var operationId = Guid.NewGuid().ToString("N");
        var operationDirectory = Path.Combine(store.TransactionDirectory, operationId);
        Directory.CreateDirectory(operationDirectory);
        var previousTargets = new List<WwmPreviousTarget>();
        var expectedTargets = new List<WwmTargetManifestEntry>();
        for (var index = 0; index < fixture.Targets.Length; index++)
        {
            var bytes = fixture.Installed[index];
            await File.WriteAllBytesAsync(Path.Combine(operationDirectory, index + ".bin"), bytes);
            previousTargets.Add(new WwmPreviousTarget
            {
                RelativePath = fixture.Targets[index], Existed = true, SizeBytes = bytes.Length,
                Sha256 = Hash(bytes), BackupFile = index + ".bin"
            });
            var future = new byte[] { (byte)(180 + index), (byte)(190 + index) };
            expectedTargets.Add(new WwmTargetManifestEntry
            {
                RelativePath = fixture.Targets[index], SizeBytes = future.Length, Sha256 = Hash(future)
            });
        }
        await store.WriteJournalAsync(new WwmTransactionJournal
        {
            SchemaVersion = 1, OperationId = operationId, Operation = "install", PreviousTargets = previousTargets,
            TargetManifest = expectedTargets, ExpectedAbsentTargets = new(), ExpectedStateSha256 = new string('a', 64),
            PreviousStateExisted = true, PreviousStateBase64 = Convert.ToBase64String(previousStateBytes), GameBuildId = detection.BuildId
        });

        var recovery = await fixture.Session.InstallService.RecoverAsync(detection);

        var stillLegacyState = store.Load(out _);
        var stillLegacyJournal = store.LoadJournal(out _);
        var filesAfter = await ReadTargetsAsync(fixture.Root, fixture.Targets);
        Assert.False(recovery.IsSuccess);
        Assert.Equal(WwmMutationError.RecoveryRequired, recovery.Error);
        Assert.Null(fixture.Session.ConfigStore.Load().Value?.GamePath);
        Assert.Equal(1, stillLegacyState!.SchemaVersion);
        Assert.Equal(activeCycleId, stillLegacyState.PreHubSnapshotCycleId);
        Assert.Equal(1, stillLegacyJournal!.SchemaVersion);
        Assert.Equal(operationId, stillLegacyJournal.OperationId);
        for (var index = 0; index < fixture.Targets.Length; index++)
            Assert.Equal(filesBefore[index], filesAfter[index]);
    }

    [Fact]
    public async Task LegacyV1StateAndSnapshot_AreNotAppliedToAnotherSteamRootWithSameBuild()
    {
        using var temp = new TestTemp();
        var (steamA, rootA) = CreateSteamRoot(temp.Root, "steam-a", "game-a");
        var (steamB, rootB) = CreateSteamRoot(temp.Root, "steam-b", "game-b");
        var detector = new WwmSteamDetector(Logger, new[] { steamA, steamB });
        var originalDetection = detector.Detect(rootA);
        var otherDetection = detector.DescribeRoot(rootB, "manual-folder-selection");
        Assert.Equal("25281753", originalDetection.BuildId);
        Assert.Equal("25281753", otherDetection.BuildId);
        Assert.Null(otherDetection.PersistedGamePath);

        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package);
        var installed = new[] { new byte[] { 10, 11 }, new byte[] { 12, 13 } };
        var previous = new[] { new byte[] { 90, 91 }, new byte[] { 92, 93 } };
        foreach (var root in new[] { rootA, rootB })
            for (var index = 0; index < targets.Length; index++)
                await File.WriteAllBytesAsync(Path.Combine(root, targets[index]), installed[index]);

        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var cycleId = Guid.NewGuid().ToString("N");
        var archive = CreatePackage();
        var package = CreatePackageMetadata(archive);
        await store.SaveAsync(new WwmInstallationState
        {
            SchemaVersion = 1, ModeSlug = package.Mode.Slug, ModeVariant = package.Mode.Variant,
            ReleaseVersion = package.Version, ArchiveSizeBytes = package.SizeBytes, ArchiveSha256 = package.Sha256,
            PreHubSnapshotCycleId = cycleId, GameBuildId = "25281753", InstalledAt = DateTimeOffset.UtcNow,
            Targets = installed.Select((bytes, index) => new WwmTargetManifestEntry
                { RelativePath = targets[index], SizeBytes = bytes.Length, Sha256 = Hash(bytes) }).ToList()
        });
        await CreatePreHubSnapshotAsync(store, targets, previous, "25281753", cycleId);
        using var client = new HttpClient(new StubHandler());
        var service = new WwmInstallService(client, paths, store, Logger);

        var availability = await service.CheckRestoreAvailabilityAsync(otherDetection);
        var restore = await service.RestorePreHubAsync(otherDetection);
        var install = await service.InstallAsync(otherDetection, CreatePackageMetadata(CreatePackage()));

        Assert.False(availability.IsAvailable);
        Assert.Contains("вручну", availability.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WwmMutationError.RecoveryRequired, restore.Error);
        Assert.Equal(WwmMutationError.RecoveryRequired, install.Error);
        Assert.Equal(1, store.Load(out _)!.SchemaVersion);
        Assert.Equal(1, System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(store.SnapshotDirectory, "snapshot.json")))
            .RootElement.GetProperty("schema_version").GetInt32());
        for (var index = 0; index < targets.Length; index++)
        {
            Assert.Equal(installed[index], await File.ReadAllBytesAsync(Path.Combine(rootA, targets[index])));
            Assert.Equal(installed[index], await File.ReadAllBytesAsync(Path.Combine(rootB, targets[index])));
        }
    }

    [Fact]
    public async Task LegacyV1Journal_CannotRecoverIntoAnotherSteamRootWithSameBuild()
    {
        using var temp = new TestTemp();
        var (steamA, _) = CreateSteamRoot(temp.Root, "steam-a", "game-a");
        var (steamB, rootB) = CreateSteamRoot(temp.Root, "steam-b", "game-b");
        var detection = new WwmSteamDetector(Logger, new[] { steamA, steamB })
            .DescribeRoot(rootB, "manual-folder-selection");
        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package);
        var previous = new[] { new byte[] { 71, 72 }, new byte[] { 81, 82 } };
        var expected = new[] { new byte[] { 31, 32 }, new byte[] { 41, 42 } };
        for (var index = 0; index < targets.Length; index++)
            await File.WriteAllBytesAsync(Path.Combine(rootB, targets[index]), expected[index]);

        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var operationId = Guid.NewGuid().ToString("N");
        var operationDirectory = Path.Combine(store.TransactionDirectory, operationId);
        Directory.CreateDirectory(operationDirectory);
        var prior = new List<WwmPreviousTarget>();
        var targetManifest = new List<WwmTargetManifestEntry>();
        for (var index = 0; index < targets.Length; index++)
        {
            await File.WriteAllBytesAsync(Path.Combine(operationDirectory, index + ".bin"), previous[index]);
            prior.Add(new WwmPreviousTarget { RelativePath = targets[index], Existed = true, SizeBytes = previous[index].Length,
                Sha256 = Hash(previous[index]), BackupFile = index + ".bin" });
            targetManifest.Add(new WwmTargetManifestEntry { RelativePath = targets[index], SizeBytes = expected[index].Length,
                Sha256 = Hash(expected[index]) });
        }
        await store.WriteJournalAsync(new WwmTransactionJournal
        {
            SchemaVersion = 1, OperationId = operationId, Operation = "install", PreviousTargets = prior,
            TargetManifest = targetManifest, ExpectedAbsentTargets = new(), ExpectedStateSha256 = "absent",
            PreviousStateExisted = false, PreviousStateBase64 = "", GameBuildId = "25281753"
        });
        using var client = new HttpClient(new StubHandler());
        var service = new WwmInstallService(client, paths, store, Logger);

        var recovered = await service.RecoverAsync(detection);

        Assert.False(recovered.IsSuccess);
        Assert.Equal(WwmMutationError.RecoveryRequired, recovered.Error);
        Assert.NotNull(store.LoadJournal(out _));
        for (var index = 0; index < targets.Length; index++)
            Assert.Equal(expected[index], await File.ReadAllBytesAsync(Path.Combine(rootB, targets[index])));
    }

    [Theory]
    [InlineData("missing_root")]
    [InlineData("relative_root")]
    [InlineData("invalid_root")]
    [InlineData("missing_layout")]
    [InlineData("invalid_layout")]
    [InlineData("numeric_layout")]
    [InlineData("bad_fingerprint")]
    [InlineData("mixed_targets")]
    public async Task MalformedSchemaV2SnapshotBinding_FailsSafely(string defect)
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "arbitrary-root");
        CreateGameMarkers(root);
        var detection = new WwmSteamDetector(Logger, Array.Empty<string>()).DescribeRoot(root, "manual-folder-selection");
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger);
        Assert.True((await service.InstallAsync(detection, CreatePackageMetadata(archive))).IsSuccess);

        var snapshotPath = Path.Combine(store.SnapshotDirectory, "snapshot.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!.AsObject();
        switch (defect)
        {
            case "missing_root": json.Remove("installation_root"); break;
            case "relative_root": json["installation_root"] = "relative\\wwm"; break;
            case "invalid_root": json["installation_root"] = Path.GetPathRoot(root) + "bad\0root"; break;
            case "missing_layout": json.Remove("target_layout"); break;
            case "invalid_layout": json["target_layout"] = "Steam"; break;
            case "numeric_layout": json["target_layout"] = "1"; break;
            case "bad_fingerprint": json["installation_fingerprint"] = "not-a-sha256"; break;
            case "mixed_targets":
                json["targets"]![0]!["relative_path"] = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.LocalDataPatch)[0];
                break;
        }
        await File.WriteAllTextAsync(snapshotPath, json.ToJsonString());
        var targets = detection.TargetLayout == WwmInstallationLayout.Package
            ? WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package)
            : throw new InvalidOperationException("Expected Package layout.");
        var installedBytes = new[] { await File.ReadAllBytesAsync(Path.Combine(root, targets[0])),
            await File.ReadAllBytesAsync(Path.Combine(root, targets[1])) };

        var availability = await service.CheckRestoreAvailabilityAsync(detection);
        var restore = await service.RestorePreHubAsync(detection);

        Assert.False(availability.IsAvailable);
        Assert.False(restore.IsSuccess);
        Assert.Equal(WwmMutationError.RecoveryRequired, restore.Error);
        Assert.True(Directory.Exists(store.SnapshotDirectory));
        for (var index = 0; index < targets.Length; index++)
            Assert.Equal(installedBytes[index], await File.ReadAllBytesAsync(Path.Combine(root, targets[index])));
    }

    [Fact]
    public async Task NonSteamStaleSnapshot_UsesNeutralInstallationChangedMessage()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "epic", "custom-root");
        CreateGameMarkers(root);
        var manifests = Path.Combine(temp.Root, "epic-manifests");
        Directory.CreateDirectory(manifests);
        File.WriteAllText(Path.Combine(manifests, "wwm.item"), System.Text.Json.JsonSerializer.Serialize(
            new { AppName = "WhereWindsMeet", InstallLocation = root }));
        var detection = new WwmSteamDetector(Logger, Array.Empty<string>(), null,
            path => Directory.EnumerateDirectories(path), epicManifestDirectory: manifests).Detect();
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger);
        Assert.True((await service.InstallAsync(detection, CreatePackageMetadata(archive))).IsSuccess);

        var executable = Path.Combine(root, WwmGameDefinition.Default.ExecutableRelativePath);
        await File.AppendAllTextAsync(executable, "changed-marker");
        var availability = await service.CheckRestoreAvailabilityAsync(detection);
        var restore = await service.RestorePreHubAsync(detection);

        Assert.False(availability.IsAvailable);
        Assert.Contains("Інсталяція Where Winds Meet змінилася", availability.Message);
        Assert.DoesNotContain("Steam", availability.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WwmMutationError.SnapshotStale, restore.Error);
        Assert.Contains("Інсталяція Where Winds Meet змінилася", restore.Message);
        Assert.DoesNotContain("Steam", restore.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(WwmInstallationLayout.Package, "random-standard-fixture")]
    [InlineData(WwmInstallationLayout.LocalDataPatch, "arbitrary-lite-fixture")]
    public async Task SyntheticWindowsLayout_InstallsAndRestoresExactPreHubState(
        WwmInstallationLayout layout, string folderName)
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, folderName);
        CreateGameMarkers(root, localDataPatch: layout == WwmInstallationLayout.LocalDataPatch);
        var detection = new WwmSteamDetector(Logger, Array.Empty<string>()).DescribeRoot(root, "manual-folder-selection");
        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(layout);
        var original = new[] { new byte[] { 31, 32 }, new byte[] { 41, 42, 43 } };
        File.WriteAllBytes(Path.Combine(root, targets[0]), original[0]);
        File.SetAttributes(Path.Combine(root, targets[0]), File.GetAttributes(Path.Combine(root, targets[0])) | FileAttributes.ReadOnly);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger);

        var installed = await service.InstallAsync(detection, CreatePackageMetadata(archive));

        Assert.True(installed.IsSuccess, installed.Message);
        var state = store.Load(out var stateError);
        Assert.Null(stateError);
        Assert.NotNull(state);
        Assert.Equal(2, state!.SchemaVersion);
        Assert.Equal(Path.GetFullPath(root), state.InstallationRoot);
        Assert.Equal(layout.ToString(), state.TargetLayout);
        Assert.All(state.Targets, item => Assert.Equal(layout, WwmStateStore.GetLayoutForPath(item.RelativePath)));
        Assert.Equal(layout == WwmInstallationLayout.Package,
            (File.GetAttributes(Path.Combine(root, targets[0])) & FileAttributes.ReadOnly) != 0);
        Assert.True((await service.CheckRestoreAvailabilityAsync(detection)).IsAvailable);

        var restored = await service.RestorePreHubAsync(detection);

        Assert.True(restored.IsSuccess, restored.Message);
        Assert.Equal(original[0], await File.ReadAllBytesAsync(Path.Combine(root, targets[0])));
        Assert.False(File.Exists(Path.Combine(root, targets[1])));
        Assert.True((File.GetAttributes(Path.Combine(root, targets[0])) & FileAttributes.ReadOnly) != 0);
        Assert.Null(store.Load(out _));
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        Assert.Null(store.LoadJournal(out _));
    }

    [Theory]
    [InlineData("unrelated", "release-standard")]
    [InlineData("arbitrary-parent", "wwm_lite")]
    public async Task OfficialStyleLocalDataPatchRootsWithArbitraryNames_AreMutatedAndRestored(
        string parentName, string gameDirectoryName)
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, parentName, "nested", gameDirectoryName);
        CreateGameMarkers(root, localDataPatch: true);
        var detection = new WwmSteamDetector(Logger, Array.Empty<string>()).DescribeRoot(root, "manual-folder-selection");
        Assert.Equal(WwmInstallationSource.UnknownWindows, detection.Source);
        Assert.Equal(WwmInstallationLayout.LocalDataPatch, detection.TargetLayout);
        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.LocalDataPatch);
        var preexisting = Path.Combine(root, targets[1]);
        var original = new byte[] { 0xAA, 0xBB };
        File.WriteAllBytes(preexisting, original);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger);

        Assert.True((await service.InstallAsync(detection, CreatePackageMetadata(archive))).IsSuccess);
        Assert.True(File.Exists(Path.Combine(root, targets[0])));
        Assert.True(File.Exists(Path.Combine(root, targets[1])));
        Assert.True((await service.RestorePreHubAsync(detection)).IsSuccess);
        Assert.False(File.Exists(Path.Combine(root, targets[0])));
        Assert.Equal(original, await File.ReadAllBytesAsync(preexisting));
    }

    [Fact]
    public async Task LocalDataPatchCrashRecovery_RestoresBothPreviousTargets()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "arbitrary", "nested", "root");
        CreateGameMarkers(root, localDataPatch: true);
        var detection = new WwmSteamDetector(Logger, Array.Empty<string>()).DescribeRoot(root, "manual-folder-selection");
        var targets = detection.TargetLayout == WwmInstallationLayout.LocalDataPatch
            ? WwmGameDefinition.Default.GetManagedRelativePaths(detection.TargetLayout)
            : throw new InvalidOperationException("Expected LocalDataPatch detection.");
        var previous = new[] { new byte[] { 11, 12 }, new byte[] { 21, 22 } };
        var installed = new[] { new byte[] { 31, 32 }, new byte[] { 41, 42 } };
        for (var i = 0; i < 2; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), previous[i]);
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var operationId = Guid.NewGuid().ToString("N");
        var operationDirectory = Path.Combine(store.TransactionDirectory, operationId);
        Directory.CreateDirectory(operationDirectory);
        var previousRecords = new List<WwmPreviousTarget>();
        var expected = new List<WwmTargetManifestEntry>();
        for (var i = 0; i < 2; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(operationDirectory, i + ".bin"), previous[i]);
            previousRecords.Add(new WwmPreviousTarget { RelativePath = targets[i], Existed = true,
                SizeBytes = previous[i].Length, Sha256 = Hash(previous[i]), BackupFile = i + ".bin" });
            expected.Add(new WwmTargetManifestEntry { RelativePath = targets[i], SizeBytes = installed[i].Length, Sha256 = Hash(installed[i]) });
        }
        File.WriteAllBytes(Path.Combine(root, targets[0]), installed[0]);
        var binding = WwmInstallationBinding.FromDetection(detection);
        var journal = new WwmTransactionJournal
        {
            SchemaVersion = 2, OperationId = operationId, Operation = "install", PreviousTargets = previousRecords,
            TargetManifest = expected, ExpectedAbsentTargets = new(), ExpectedStateSha256 = Hash(new byte[] { 1 }),
            InstallationRoot = Path.GetFullPath(root), TargetLayout = detection.TargetLayout.ToString(),
            InstallationFingerprint = binding.Fingerprint, PreviousStateBase64 = "", PreviousStateExisted = false
        };
        await store.WriteJournalAsync(journal);
        using var client = new HttpClient(new StubHandler());
        var service = new WwmInstallService(client, paths, store, Logger);

        var recovered = await service.RecoverAsync(detection);

        Assert.True(recovered.IsSuccess, recovered.Message);
        for (var i = 0; i < 2; i++) Assert.Equal(previous[i], await File.ReadAllBytesAsync(Path.Combine(root, targets[i])));
        Assert.Null(store.LoadJournal(out _));
    }

    [Fact]
    public async Task UnknownDualLayout_IsFoundButMutationIsBlocked()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "dual-root");
        CreateGameMarkers(root);
        Directory.CreateDirectory(Path.Combine(root, WwmGameDefinition.Default.LocalDataPatchLocaleRelativePath));
        var detection = new WwmSteamDetector(Logger, Array.Empty<string>()).DescribeRoot(root, "manual-folder-selection");
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var result = await new WwmInstallService(client, paths, new WwmStateStore(paths), Logger)
            .InstallAsync(detection, CreatePackageMetadata(archive));

        Assert.True(detection.IsFound);
        Assert.True(detection.HasAmbiguousTargetLayout);
        Assert.False(detection.IsMutationSupported);
        Assert.Equal(WwmMutationError.InvalidRoot, result.Error);
        Assert.Empty(Directory.EnumerateFiles(root, "translate_words_map_en*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task InstallationStateCannotBeRestoredToAnotherRootOrLayout()
    {
        using var temp = new TestTemp();
        var rootA = Path.Combine(temp.Root, "root-a");
        var rootB = Path.Combine(temp.Root, "root-b");
        CreateGameMarkers(rootA);
        CreateGameMarkers(rootB, localDataPatch: true);
        var detector = new WwmSteamDetector(Logger, Array.Empty<string>());
        var first = detector.DescribeRoot(rootA, "manual-folder-selection");
        var second = detector.DescribeRoot(rootB, "manual-folder-selection");
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var service = new WwmInstallService(client, paths, new WwmStateStore(paths), Logger);
        Assert.True((await service.InstallAsync(first, CreatePackageMetadata(archive))).IsSuccess);

        var availability = await service.CheckRestoreAvailabilityAsync(second);
        var restore = await service.RestorePreHubAsync(second);

        Assert.False(availability.IsAvailable);
        Assert.Equal(WwmMutationError.RecoveryRequired, restore.Error);
        Assert.True(File.Exists(Path.Combine(rootA, WwmGameDefinition.Default.ManagedRelativePaths[0])));
        Assert.Empty(Directory.EnumerateFiles(rootB, "translate_words_map_en*", SearchOption.AllDirectories));
    }

    [Fact]
    public void ManualResolver_SearchesSelectedAncestorRecursivelyWithoutRequiringKnownFolderNames()
    {
        using var temp = new TestTemp();
        var selected = Path.Combine(temp.Root, "outer", "parent");
        var root = Path.Combine(selected, "arbitrary", "nested", "game");
        CreateGameMarkers(root);

        var result = new WwmSteamDetector(Logger, Array.Empty<string>()).ResolveManualRoot(selected);

        Assert.Equal(ManualResolveStatus.Found, result.Status);
        Assert.Equal(Path.GetFullPath(root), result.GamePath);
    }

    [Fact]
    public void ManualResolver_UsesSelectedDirectoryWhenItIsTheRoot()
    {
        using var temp = new TestTemp();
        CreateGameMarkers(temp.Root);

        var result = new WwmSteamDetector(Logger, Array.Empty<string>()).ResolveManualRoot(temp.Root);

        Assert.Equal(ManualResolveStatus.Found, result.Status);
        Assert.Equal(Path.GetFullPath(temp.Root), result.GamePath);
    }

    [Fact]
    public void ManualResolver_ReturnsNotFoundForNoCandidates()
    {
        using var temp = new TestTemp();
        Directory.CreateDirectory(Path.Combine(temp.Root, "deep", "empty"));

        var result = new WwmSteamDetector(Logger, Array.Empty<string>()).ResolveManualRoot(temp.Root);

        Assert.Equal(ManualResolveStatus.NotFound, result.Status);
    }

    [Fact]
    public void ManualResolver_ReturnsAmbiguousForMultipleValidDescendants()
    {
        using var temp = new TestTemp();
        CreateGameMarkers(Path.Combine(temp.Root, "first", "game"));
        CreateGameMarkers(Path.Combine(temp.Root, "second", "game"));

        var result = new WwmSteamDetector(Logger, Array.Empty<string>()).ResolveManualRoot(temp.Root);

        Assert.Equal(ManualResolveStatus.Ambiguous, result.Status);
        Assert.Null(result.GamePath);
    }

    [Fact]
    public void ManualResolver_InaccessibleSubtreeNeverProducesConfidentFound()
    {
        using var temp = new TestTemp();
        var inaccessible = Path.Combine(temp.Root, "inaccessible");
        Directory.CreateDirectory(inaccessible);
        var root = Path.Combine(temp.Root, "available", "nested");
        CreateGameMarkers(root);
        IEnumerable<string> Enumerate(string path)
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(inaccessible), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("synthetic access denied");
            return Directory.GetDirectories(path);
        }
        var detector = new WwmSteamDetector(Logger, Array.Empty<string>(), null, Enumerate);

        var result = detector.ResolveManualRoot(temp.Root);

        Assert.Equal(ManualResolveStatus.Incomplete, result.Status);
        Assert.Null(result.GamePath);
    }

    [Fact]
    public async Task ManualResolverAsync_RunsRecursiveTraversalOffCallingThread()
    {
        using var temp = new TestTemp();
        var selected = Path.Combine(temp.Root, "selected-parent");
        var root = Path.Combine(selected, "nested", "arbitrary-root");
        CreateGameMarkers(root);
        var traversalThreadId = 0;

        IEnumerable<string> Enumerate(string path)
        {
            Interlocked.CompareExchange(ref traversalThreadId, Environment.CurrentManagedThreadId, 0);
            return Directory.EnumerateDirectories(path);
        }

        using var client = new HttpClient(new StubHandler());
        using var session = WwmGameSession.CreateForTests(
            new AppPaths(Path.Combine(temp.Root, "app")), Logger, client,
            manualDirectoryEnumerator: Enumerate);

        var completion = new TaskCompletionSource<(int CallerThreadId, WwmManualDetectionResult Result)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callerThread = new Thread(() =>
        {
            try
            {
                var callerThreadId = Environment.CurrentManagedThreadId;
                var result = session.ResolveAndSaveManualPathAsync(selected).GetAwaiter().GetResult();
                completion.TrySetResult((callerThreadId, result));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        callerThread.Start();
        var outcome = await completion.Task;
        callerThread.Join();

        Assert.Equal(ManualResolveStatus.Found, outcome.Result.Status);
        Assert.NotEqual(outcome.CallerThreadId, Volatile.Read(ref traversalThreadId));
        Assert.Equal(Path.GetFullPath(root), session.ConfigStore.Load().Value!.GamePath);
    }

    [Fact]
    public async Task ManualResolver_ObservesCancellationDuringTraversal()
    {
        using var temp = new TestTemp();
        var selected = Path.Combine(temp.Root, "selected");
        var first = Directory.CreateDirectory(Path.Combine(selected, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(selected, "second")).FullName;
        using var cancellation = new CancellationTokenSource();

        IEnumerable<string> Enumerate(string path)
        {
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(selected), StringComparison.OrdinalIgnoreCase))
                return Array.Empty<string>();

            return YieldAndCancel();

            IEnumerable<string> YieldAndCancel()
            {
                yield return first;
                cancellation.Cancel();
                yield return second;
            }
        }

        using var client = new HttpClient(new StubHandler());
        using var session = WwmGameSession.CreateForTests(
            new AppPaths(Path.Combine(temp.Root, "app")), Logger, client,
            manualDirectoryEnumerator: Enumerate);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.ResolveAndSaveManualPathAsync(selected, cancellation.Token));
    }

    [Fact]
    public async Task ManualResolver_SafetyLimitWithCandidateAndPendingTreeReturnsIncomplete()
    {
        using var temp = new TestTemp();
        var selected = Path.Combine(temp.Root, "selected");
        CreateGameMarkers(Path.Combine(selected, "a-game"));
        Directory.CreateDirectory(Path.Combine(selected, "b-branch", "unvisited-child"));
        var detector = new WwmSteamDetector(Logger, Array.Empty<string>(), null,
            path => Directory.EnumerateDirectories(path), maximumManualDirectories: 2);

        var result = await Task.Run(() => detector.ResolveManualRoot(selected));

        Assert.Equal(ManualResolveStatus.Incomplete, result.Status);
        Assert.Null(result.GamePath);
    }

    [Fact]
    public async Task ManualResolver_IncompleteResultDoesNotPersistSelectedGamePath()
    {
        using var temp = new TestTemp();
        var selected = Path.Combine(temp.Root, "selected");
        CreateGameMarkers(Path.Combine(selected, "a-game"));
        Directory.CreateDirectory(Path.Combine(selected, "b-branch", "unvisited-child"));
        using var client = new HttpClient(new StubHandler());
        using var session = WwmGameSession.CreateForTests(
            new AppPaths(Path.Combine(temp.Root, "app")), Logger, client,
            maximumManualDirectories: 2);

        var result = await session.ResolveAndSaveManualPathAsync(selected);

        Assert.Equal(ManualResolveStatus.Incomplete, result.Status);
        Assert.Null(result.Detection);
        Assert.Null(session.ConfigStore.Load().Value?.GamePath);
    }

    [Fact]
    public void ManualResolver_DoesNotFollowDirectoryJunctions()
    {
        using var temp = new TestTemp();
        var selected = Path.Combine(temp.Root, "selected");
        var external = Path.Combine(temp.Root, "outside-tree", "valid-game");
        var junction = Path.Combine(selected, "linked-game");
        Directory.CreateDirectory(selected);
        CreateGameMarkers(external);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junction}\" \"{external}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);

        try
        {
            var result = new WwmSteamDetector(Logger, Array.Empty<string>()).ResolveManualRoot(selected);
            Assert.Equal(ManualResolveStatus.NotFound, result.Status);
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public void CoexistingLayoutsAndSavedRootDoNotProvePlatform()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "custom-install");
        CreateGameMarkers(root);
        Directory.CreateDirectory(Path.Combine(root, WwmGameDefinition.Default.LocalDataPatchLocaleRelativePath));
        var detector = new WwmSteamDetector(Logger, Array.Empty<string>());

        var result = detector.Detect(root);

        Assert.Equal(WwmInstallationLayout.Package | WwmInstallationLayout.LocalDataPatch, result.Layout);
        Assert.Equal(WwmInstallationSource.UnknownWindows, result.Source);
        Assert.Null(result.BuildId);
        Assert.Equal("saved-config", result.EvidenceMarker);
    }

    [Fact]
    public async Task PackageService_StagesOnlyApprovedTwoFilesAndVerifiesOuterHash()
    {
        using var temp = new TestTemp();
        var bytes = CreatePackage();
        var package = CreatePackageMetadata(bytes);
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var service = new WwmPackageService(client, paths, Logger);

        var staged = await service.DownloadAndStageAsync(package, CancellationToken.None);
        try
        {
            Assert.Equal(2, staged.Targets.Count);
            Assert.All(staged.Targets, target => Assert.True(File.Exists(Path.Combine(staged.WorkDirectory, "staged", target.RelativePath))));
        }
        finally { service.Cleanup(staged.WorkDirectory); }
    }

    [Fact]
    public async Task PackageService_RejectsTraversalBeforeAnyGameMutation()
    {
        using var temp = new TestTemp();
        var bytes = CreatePackage(("../outside.bin", new byte[] { 9 }));
        var package = CreatePackageMetadata(bytes);
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        await Assert.ThrowsAsync<InvalidDataException>(() => new WwmPackageService(client, paths, Logger)
            .DownloadAndStageAsync(package, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(temp.Root, "outside.bin")));
    }

    [Theory]
    [InlineData("C:/outside.bin")]
    [InlineData("Package/HD/oversea/locale/unexpected.bin")]
    [InlineData("Package/HD/oversea/locale/translate_words_map_en")]
    public async Task PackageService_RejectsAbsoluteUnexpectedAndDuplicateEntries(string extraPath)
    {
        using var temp = new TestTemp();
        var archive = CreatePackage((extraPath, new byte[] { 20 }));
        var package = CreatePackageMetadata(archive);
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        await Assert.ThrowsAsync<InvalidDataException>(() => new WwmPackageService(client, paths, Logger)
            .DownloadAndStageAsync(package, CancellationToken.None));
    }

    [Fact]
    public async Task PackageService_RejectsOuterSizeAndShaMismatch()
    {
        using var temp = new TestTemp();
        var archive = CreatePackage();
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var service = new WwmPackageService(client, paths, Logger);
        var sizeMismatch = WithSize(CreatePackageMetadata(archive), archive.Length + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync(sizeMismatch, CancellationToken.None));
        var hashMismatch = new WwmApiPackage { Mode = Feed(true).Data!.Modes![0], Version = "v-test",
            DownloadUrl = "https://winds4ua.com.ua/package.zip", SizeBytes = archive.Length, Sha256 = new string('b', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync(hashMismatch, CancellationToken.None));

        static WwmApiPackage WithSize(WwmApiPackage source, long size) => new()
        {
            Mode = source.Mode, Version = source.Version, DownloadUrl = source.DownloadUrl, SizeBytes = size, Sha256 = source.Sha256
        };
    }

    [Fact]
    public async Task PackageService_RejectsMissingRequiredTargetAndSymlinkEntry()
    {
        using var temp = new TestTemp();
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = new WwmPackageService(client, paths, Logger);

        var incomplete = CreateIncompletePackage();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync(
            CreatePackageMetadata(incomplete), CancellationToken.None));

        var linked = CreatePackageWithSymlink();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync(
            CreatePackageMetadata(linked), CancellationToken.None));
        Assert.Empty(Directory.Exists(Path.Combine(paths.CacheDir, "packages"))
            ? Directory.GetDirectories(Path.Combine(paths.CacheDir, "packages"))
            : Array.Empty<string>());
    }

    [Fact]
    public async Task InstallAndRestore_ReturnsExactPreHubFilesAndDeletesPreviouslyAbsentTarget()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var original = new byte[] { 3, 4, 5 };
        var originalTarget = Path.Combine(root, targets[0]);
        File.WriteAllBytes(originalTarget, original);
        File.SetAttributes(originalTarget, File.GetAttributes(originalTarget) | FileAttributes.ReadOnly);
        var packageBytes = CreatePackage();
        var package = CreatePackageMetadata(packageBytes);
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(packageBytes) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger);

        var installed = await service.InstallAsync(root, "42", package);
        Assert.True(installed.IsSuccess, installed.Message + " | " + string.Join(" || ", Logger.Errors));
        Assert.True(File.Exists(Path.Combine(root, targets[1])));
        Assert.NotNull(store.Load(out var error));
        Assert.Null(error);
        var restoreAvailability = await service.CheckRestoreAvailabilityAsync(root, "42");
        Assert.True(restoreAvailability.IsAvailable, restoreAvailability.Message);

        var restored = await service.RestorePreHubAsync(root, "42");
        Assert.True(restored.IsSuccess, restored.Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(originalTarget));
        Assert.True((File.GetAttributes(originalTarget) & FileAttributes.ReadOnly) != 0);
        Assert.False(File.Exists(Path.Combine(root, targets[1])));
        Assert.Null(store.Load(out error));
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        Assert.Null(store.LoadJournal(out error));
        Assert.Null(error);
        Assert.False((await service.CheckRestoreAvailabilityAsync(root, "42")).IsAvailable);
    }

    [Fact]
    public async Task RestoreAvailability_RequiresValidOwnedInitialSnapshot()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        File.WriteAllBytes(Path.Combine(root, WwmGameDefinition.Default.ManagedRelativePaths[0]), new byte[] { 21, 22, 23 });
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger);
        var installed = await service.InstallAsync(root, "42", CreatePackageMetadata(archive));
        Assert.True(installed.IsSuccess, installed.Message);

        File.Delete(Path.Combine(store.SnapshotDirectory, "0.bin"));
        var unavailable = await service.CheckRestoreAvailabilityAsync(root, "42");

        Assert.False(unavailable.IsAvailable);
        Assert.Contains("Відновлення оригіналу неможливе", unavailable.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, WwmGameDefinition.Default.ManagedRelativePaths[0])));
    }

    [Fact]
    public async Task SuccessfulUpdate_PreservesOriginalPreHubSnapshot()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var original = new[] { new byte[] { 3, 4, 5 }, new byte[] { 6, 7, 8 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), original[i]);
        var firstArchive = CreatePackage();
        var updatedArchive = CreatePackageWithContents(new byte[] { 91, 92 }, new byte[] { 93, 94 });
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(firstArchive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger);
        Assert.True((await service.InstallAsync(root, "42", CreatePackageMetadata(firstArchive))).IsSuccess);
        var snapshotBefore = Directory.GetFiles(store.SnapshotDirectory)
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        using var updatedClient = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(updatedArchive) });
        var updatedService = new WwmInstallService(updatedClient, paths, store, Logger);
        var updated = await updatedService.InstallAsync(root, "42", CreatePackageMetadata(updatedArchive));

        Assert.True(updated.IsSuccess, updated.Message);
        Assert.Equal(snapshotBefore.Keys.Order(), Directory.GetFiles(store.SnapshotDirectory).Select(Path.GetFileName).Order());
        foreach (var (name, bytes) in snapshotBefore)
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(store.SnapshotDirectory, name)));
        var restored = await updatedService.RestorePreHubAsync(root, "42");
        Assert.True(restored.IsSuccess, restored.Message);
        for (var i = 0; i < targets.Length; i++) Assert.Equal(original[i], await File.ReadAllBytesAsync(Path.Combine(root, targets[i])));
        AssertNoGameTemps(root);
    }

    [Fact]
    public async Task Install_FailureAfterFirstTargetRollsBackAllTargetsAndState()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var firstOriginal = new byte[] { 1, 2, 3 };
        var secondOriginal = new byte[] { 4, 5, 6 };
        File.WriteAllBytes(Path.Combine(root, targets[0]), firstOriginal);
        File.WriteAllBytes(Path.Combine(root, targets[1]), secondOriginal);
        foreach (var relative in targets)
        {
            var target = Path.Combine(root, relative);
            File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
        }
        var archive = CreatePackage();
        var package = CreatePackageMetadata(archive);
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger) { AfterTargetAppliedForTest = count =>
        {
            if (count == 1) throw new IOException("Injected failure after first managed target.");
        }};

        var result = await service.InstallAsync(root, "42", package);

        Assert.False(result.IsSuccess);
        Assert.Equal(WwmMutationError.Mutation, result.Error);
        Assert.Equal(firstOriginal, await File.ReadAllBytesAsync(Path.Combine(root, targets[0])));
        Assert.Equal(secondOriginal, await File.ReadAllBytesAsync(Path.Combine(root, targets[1])));
        Assert.All(targets, relative => Assert.True((File.GetAttributes(Path.Combine(root, relative)) & FileAttributes.ReadOnly) != 0));
        Assert.Null(store.Load(out _));
        Assert.Null(store.LoadJournal(out var journalError));
        Assert.Null(journalError);
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        AssertNoGameTemps(root);
    }

    [Fact]
    public async Task LocalDataPatchInstall_FailureAfterFirstTargetRestoresReadOnlyAttributesAndBytes()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "arbitrary-localdata-root");
        CreateGameMarkers(root, localDataPatch: true);
        var detection = new WwmSteamDetector(Logger, Array.Empty<string>()).DescribeRoot(root, "manual-folder-selection");
        var targets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.LocalDataPatch);
        var originals = new[] { new byte[] { 15, 16 }, new byte[] { 25, 26, 27 } };
        foreach (var (relative, index) in targets.Select((relative, index) => (relative, index)))
        {
            var target = Path.Combine(root, relative);
            File.WriteAllBytes(target, originals[index]);
            File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
        }

        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger)
        {
            AfterTargetAppliedForTest = count =>
            {
                if (count == 1) throw new IOException("Injected failure after LocalDataPatch target replacement.");
            }
        };

        var result = await service.InstallAsync(detection, CreatePackageMetadata(archive));

        Assert.False(result.IsSuccess);
        Assert.Equal(WwmMutationError.Mutation, result.Error);
        for (var index = 0; index < targets.Length; index++)
        {
            var target = Path.Combine(root, targets[index]);
            Assert.Equal(originals[index], await File.ReadAllBytesAsync(target));
            Assert.True((File.GetAttributes(target) & FileAttributes.ReadOnly) != 0);
        }
        Assert.Null(store.Load(out _));
        Assert.Null(store.LoadJournal(out _));
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        AssertNoGameTemps(root);
    }

    [Fact]
    public async Task Install_GameTempWriteFailureDoesNotAttemptRollbackAndLogsExactPhase()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var originals = new[] { new byte[] { 11, 12 }, new byte[] { 13, 14 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), originals[i]);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var logger = new NullLogger();
        var service = new WwmInstallService(client, paths, store, logger)
        {
            BeforeGameTempWriteForTest = (_, path) =>
            {
                File.WriteAllBytes(path, new byte[] { 99 });
                throw new UnauthorizedAccessException("Injected temp preparation denial.");
            }
        };

        var result = await service.InstallAsync(root, "42", CreatePackageMetadata(archive));

        Assert.Equal(WwmMutationError.Mutation, result.Error);
        for (var i = 0; i < targets.Length; i++)
            Assert.Equal(originals[i], await File.ReadAllBytesAsync(Path.Combine(root, targets[i])));
        AssertNoGameTemps(root);
        Assert.Null(store.LoadJournal(out _));
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        var diagnostic = Assert.Single(logger.Errors, message => message.Contains("operation=install", StringComparison.Ordinal));
        Assert.Contains("target=" + targets[0], diagnostic, StringComparison.Ordinal);
        Assert.Contains("phase=write_game_temp", diagnostic, StringComparison.Ordinal);
        Assert.Contains("exception=UnauthorizedAccessException", diagnostic, StringComparison.Ordinal);
        Assert.Contains("hresult=0x80070005 win32_error=5", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Errors, message => message.Contains("operation=rollback", StringComparison.Ordinal));
    }

    [Fact]
    public void WindowsFileReplace_ReadOnlyManagedTargetReproducesAccessDenied()
    {
        using var temp = new TestTemp();
        var target = Path.Combine(temp.Root, "target.bin");
        var replacement = Path.Combine(temp.Root, "target.tmp");
        File.WriteAllBytes(target, new byte[] { 1 });
        File.WriteAllBytes(replacement, new byte[] { 2 });
        File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);

        var exception = Record.Exception(() => File.Replace(replacement, target, null));

        Assert.NotNull(exception);
        Assert.Equal(unchecked((int)0x80070005), exception.HResult);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(target));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(replacement));
    }

    [Fact]
    public async Task Install_ReadOnlyTargetReplaceFailureWithNoTargetChangeSkipsDestructiveRollback()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var originals = new[] { new byte[] { 21, 22 }, new byte[] { 23, 24 } };
        foreach (var (relative, index) in targets.Select((relative, index) => (relative, index)))
        {
            var target = Path.Combine(root, relative);
            File.WriteAllBytes(target, originals[index]);
            File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
        }
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var logger = new NullLogger();
        var service = new WwmInstallService(client, paths, store, logger)
        {
            BeforeTargetReplaceForTest = (_, _) => throw new UnauthorizedAccessException("Injected replace denial.")
        };

        var result = await service.InstallAsync(root, "42", CreatePackageMetadata(archive));

        Assert.Equal(WwmMutationError.Mutation, result.Error);
        for (var i = 0; i < targets.Length; i++)
        {
            var target = Path.Combine(root, targets[i]);
            Assert.Equal(originals[i], await File.ReadAllBytesAsync(target));
            Assert.True((File.GetAttributes(target) & FileAttributes.ReadOnly) != 0);
        }
        Assert.Null(store.LoadJournal(out _));
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        AssertNoGameTemps(root);
        var diagnostic = Assert.Single(logger.Errors, message => message.Contains("operation=install", StringComparison.Ordinal));
        Assert.Contains("phase=replace_target", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Errors, message => message.Contains("operation=rollback", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedFirstInstall_RetiresSnapshotAndNextCycleCapturesFreshBaseline()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var firstBaseline = new[] { new byte[] { 1, 2 }, new byte[] { 3, 4 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), firstBaseline[i]);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var failing = new WwmInstallService(client, paths, store, Logger)
        {
            AfterTargetAppliedForTest = count => { if (count == 1) throw new IOException("Injected first-install failure."); }
        };

        var failed = await failing.InstallAsync(root, "42", CreatePackageMetadata(archive));
        Assert.Equal(WwmMutationError.Mutation, failed.Error);
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        AssertNoGameTemps(root);

        var changedBaseline = new[] { new byte[] { 21, 22 }, new byte[] { 23, 24 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), changedBaseline[i]);
        var nextCycle = new WwmInstallService(client, paths, store, Logger);
        var installed = await nextCycle.InstallAsync(root, "42", CreatePackageMetadata(archive));
        Assert.True(installed.IsSuccess, installed.Message);
        Assert.Equal(changedBaseline[0], await File.ReadAllBytesAsync(Path.Combine(store.SnapshotDirectory, "0.bin")));
        var restored = await nextCycle.RestorePreHubAsync(root, "42");
        Assert.True(restored.IsSuccess, restored.Message);
        for (var i = 0; i < targets.Length; i++)
            Assert.Equal(changedBaseline[i], await File.ReadAllBytesAsync(Path.Combine(root, targets[i])));
        AssertNoGameTemps(root);
    }

    [Fact]
    public async Task Install_ExistingSnapshotWithoutTrustedStateFailsClosed()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var before = new[] { new byte[] { 7 }, new byte[] { 8 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), before[i]);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        Directory.CreateDirectory(store.SnapshotDirectory);
        var sentinel = Path.Combine(store.SnapshotDirectory, "untrusted-evidence.bin");
        await File.WriteAllBytesAsync(sentinel, new byte[] { 99 });

        var result = await new WwmInstallService(client, paths, store, Logger)
            .InstallAsync(root, "42", CreatePackageMetadata(archive));

        Assert.Equal(WwmMutationError.RecoveryRequired, result.Error);
        Assert.Equal(new byte[] { 99 }, await File.ReadAllBytesAsync(sentinel));
        for (var i = 0; i < targets.Length; i++) Assert.Equal(before[i], await File.ReadAllBytesAsync(Path.Combine(root, targets[i])));
    }

    [Fact]
    public async Task Recover_AfterRollbackReplaceRemovesOperationOwnedTempsAndIsIdempotent()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var previous = new[] { new byte[] { 1, 2 }, new byte[] { 3, 4 } };
        var expected = new[] { new byte[] { 5, 6 }, new byte[] { 7, 8 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), previous[i]);
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(new HttpClient(new StubHandler()), paths, store, Logger);
        var journal = PrepareJournal(store, root, previous, expected, targets, new string('a', 64));
        var target = Path.Combine(root, targets[0]);
        File.WriteAllBytes(target, previous[0]);
        await File.WriteAllBytesAsync(target + ".hub-" + journal.OperationId + ".tmp", new byte[] { 1 });
        await File.WriteAllBytesAsync(target + ".rollback-" + journal.OperationId + ".tmp", new byte[] { 2 });
        var unrelatedOperationTemp = target + ".rollback-" + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(unrelatedOperationTemp, new byte[] { 3 });

        var first = await service.RecoverAsync(root, "42");
        var second = await service.RecoverAsync(root, "42");

        Assert.True(first.IsSuccess, first.Message);
        Assert.True(second.IsSuccess, second.Message);
        for (var i = 0; i < targets.Length; i++) Assert.Equal(previous[i], await File.ReadAllBytesAsync(Path.Combine(root, targets[i])));
        Assert.False(File.Exists(target + ".hub-" + journal.OperationId + ".tmp"));
        Assert.False(File.Exists(target + ".rollback-" + journal.OperationId + ".tmp"));
        Assert.True(File.Exists(unrelatedOperationTemp));
    }

    [Fact]
    public async Task Recover_AbortedFirstManagementRetiresSnapshotAndNextInstallUsesFreshBaseline()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var previous = new[] { new byte[] { 11, 12 }, new byte[] { 13, 14 } };
        var expected = new[] { new byte[] { 5, 6 }, new byte[] { 7, 8 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), previous[i]);
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var journal = PrepareJournal(store, root, previous, expected, targets, new string('a', 64), createdSnapshot: true);
        File.WriteAllBytes(Path.Combine(root, targets[0]), expected[0]);
        var service = new WwmInstallService(new HttpClient(new StubHandler()), paths, store, Logger);

        var recovered = await service.RecoverAsync(root, "42");

        Assert.True(recovered.IsSuccess, recovered.Message);
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        for (var i = 0; i < targets.Length; i++) Assert.Equal(previous[i], await File.ReadAllBytesAsync(Path.Combine(root, targets[i])));
        var changedBaseline = new[] { new byte[] { 31, 32 }, new byte[] { 33, 34 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), changedBaseline[i]);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var nextService = new WwmInstallService(client, paths, store, Logger);
        Assert.True((await nextService.InstallAsync(root, "42", CreatePackageMetadata(archive))).IsSuccess);
        var restored = await nextService.RestorePreHubAsync(root, "42");
        Assert.True(restored.IsSuccess, restored.Message);
        for (var i = 0; i < targets.Length; i++) Assert.Equal(changedBaseline[i], await File.ReadAllBytesAsync(Path.Combine(root, targets[i])));
        AssertNoGameTemps(root);
    }

    [Fact]
    public async Task Recover_OrphanSnapshotWithoutJournalRequiresExactBaselineEvidence()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var baseline = new[] { new byte[] { 15, 16 }, new byte[] { 17, 18 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), baseline[i]);
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var cycleId = Guid.NewGuid().ToString("N");
        await CreatePreHubSnapshotAsync(store, targets, baseline, "42", cycleId, root);
        var orphanOperationDir = Path.Combine(store.TransactionDirectory, cycleId);
        Directory.CreateDirectory(orphanOperationDir);
        await File.WriteAllBytesAsync(Path.Combine(orphanOperationDir, "0.bin"), new byte[] { 1 });
        var service = new WwmInstallService(new HttpClient(new StubHandler()), paths, store, Logger);

        var recovered = await service.RecoverAsync(root, "42");
        Assert.True(recovered.IsSuccess, recovered.Message);
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        Assert.False(Directory.Exists(orphanOperationDir));

        await CreatePreHubSnapshotAsync(store, targets, baseline, "42", Guid.NewGuid().ToString("N"), root);
        File.WriteAllBytes(Path.Combine(root, targets[0]), new byte[] { 99, 100 });
        var ambiguous = await service.RecoverAsync(root, "42");
        Assert.Equal(WwmMutationError.RecoveryRequired, ambiguous.Error);
        Assert.True(Directory.Exists(store.SnapshotDirectory));
        Assert.Equal(new byte[] { 99, 100 }, await File.ReadAllBytesAsync(Path.Combine(root, targets[0])));
    }

    [Fact]
    public async Task Restore_PreparationCancellationReturnsTypedResultAndCleansTransactionDirectory()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), new byte[] { (byte)(i + 1) });
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        Assert.True((await new WwmInstallService(client, paths, store, Logger).InstallAsync(root, "42", CreatePackageMetadata(archive))).IsSuccess);
        using var cancellation = new CancellationTokenSource();
        var service = new WwmInstallService(client, paths, store, Logger)
        {
            BeforeRestoreJournalForTest = cancellation.Cancel
        };

        var result = await service.RestorePreHubAsync(root, "42", cancellation.Token);

        Assert.Equal(WwmMutationError.Cancelled, result.Error);
        Assert.False(File.Exists(store.JournalFile));
        Assert.Empty(Directory.Exists(store.TransactionDirectory) ? Directory.GetDirectories(store.TransactionDirectory) : Array.Empty<string>());
        Assert.NotNull(store.Load(out var stateError));
        Assert.Null(stateError);
        AssertNoGameTemps(root);
    }

    [Fact]
    public async Task Install_StateSaveFailureRollsBackFilesAndExactPreviousStateBytes()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var original = new[] { new byte[] { 31, 32 }, new byte[] { 41, 42 } };
        foreach (var index in Enumerable.Range(0, targets.Length))
            File.WriteAllBytes(Path.Combine(root, targets[index]), original[index]);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var snapshotCycleId = Guid.NewGuid().ToString("N");
        var previousStateModel = CreateState(targets, original);
        previousStateModel.PreHubSnapshotCycleId = snapshotCycleId;
        previousStateModel.SchemaVersion = 2;
        previousStateModel.InstallationRoot = Path.GetFullPath(root);
        previousStateModel.TargetLayout = WwmInstallationLayout.Package.ToString();
        previousStateModel.InstallationFingerprint = WwmGameDefinition.ComputeInstallationFingerprint(root);
        await CreatePreHubSnapshotAsync(store, targets, original, "42", snapshotCycleId, root);
        await store.SaveAsync(previousStateModel);
        var previousState = await File.ReadAllBytesAsync(store.StateFile);
        var service = new WwmInstallService(client, paths, store, Logger)
        {
            BeforeStateSaveForTest = () => throw new IOException("Injected state commit failure.")
        };

        var result = await service.InstallAsync(root, "42", CreatePackageMetadata(archive));

        Assert.False(result.IsSuccess);
        Assert.Equal(WwmMutationError.Mutation, result.Error);
        for (var index = 0; index < targets.Length; index++)
            Assert.Equal(original[index], await File.ReadAllBytesAsync(Path.Combine(root, targets[index])));
        Assert.Equal(previousState, await File.ReadAllBytesAsync(store.StateFile));
        Assert.Null(store.LoadJournal(out var journalError));
        Assert.Null(journalError);
    }

    [Fact]
    public async Task Install_RollbackFailureRetainsJournalAndReturnsCriticalFailure()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        File.WriteAllBytes(Path.Combine(root, targets[0]), new byte[] { 1, 2 });
        File.WriteAllBytes(Path.Combine(root, targets[1]), new byte[] { 3, 4 });
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger)
        {
            AfterTargetAppliedForTest = count =>
            {
                if (count != 1) return;
                var operation = Directory.GetDirectories(store.TransactionDirectory).Single();
                File.WriteAllBytes(Path.Combine(operation, "0.bin"), new byte[] { 99 });
                throw new IOException("Injected failure with damaged rollback evidence.");
            }
        };

        var result = await service.InstallAsync(root, "42", CreatePackageMetadata(archive));

        Assert.False(result.IsSuccess);
        Assert.Equal(WwmMutationError.RollbackFailed, result.Error);
        Assert.True(File.Exists(store.JournalFile));
        Assert.True(Directory.Exists(store.TransactionDirectory));
        Assert.Contains(Logger.Errors, message => message.Contains("rollback backup failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Install_CancellationBeforeDownloadDoesNotMutateGame()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var paths = WwmGameDefinition.Default.ManagedRelativePaths;
        var original = new byte[] { 22, 23 };
        File.WriteAllBytes(Path.Combine(root, paths[0]), original);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new WwmInstallService(client, scope, new WwmStateStore(scope), Logger)
            .InstallAsync(root, "42", CreatePackageMetadata(archive), cancellation.Token);

        Assert.Equal(WwmMutationError.Cancelled, result.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(root, paths[0])));
        Assert.False(File.Exists(Path.Combine(root, paths[1])));
        var packageCache = Path.Combine(scope.CacheDir, "packages");
        Assert.True(!Directory.Exists(packageCache) || Directory.GetDirectories(packageCache).Length == 0);
    }

    [Fact]
    public async Task Install_CancellationAfterFirstTargetRollsBackBothTargets()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var original = new[] { new byte[] { 1, 4 }, new byte[] { 2, 5 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), original[i]);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        using var cancellation = new CancellationTokenSource();
        var service = new WwmInstallService(client, scope, new WwmStateStore(scope), Logger)
        {
            AfterTargetAppliedForTest = count => { if (count == 1) cancellation.Cancel(); }
        };

        var result = await service.InstallAsync(root, "42", CreatePackageMetadata(archive), cancellation.Token);

        Assert.Equal(WwmMutationError.Cancelled, result.Error);
        for (var i = 0; i < targets.Length; i++) Assert.Equal(original[i], await File.ReadAllBytesAsync(Path.Combine(root, targets[i])));
        Assert.Null(new WwmStateStore(scope).Load(out _));
    }

    [Fact]
    public async Task Recover_RollsBackPartialTransactionAndPreservesFullyCommittedTransaction()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var old = new[] { new byte[] { 1, 2 }, new byte[] { 3, 4 } };
        var next = new[] { new byte[] { 5, 6 }, new byte[] { 7, 8 } };
        for (var i = 0; i < 2; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), old[i]);
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(new HttpClient(new StubHandler()), paths, store, Logger);
        var operation = PrepareJournal(store, root, old, next, targets, stateHash: new string('a', 64));
        File.WriteAllBytes(Path.Combine(root, targets[0]), next[0]);

        var recovered = await service.RecoverAsync(root, "42");
        Assert.True(recovered.IsSuccess, recovered.Message);
        Assert.Equal(old[0], await File.ReadAllBytesAsync(Path.Combine(root, targets[0])));
        Assert.Equal(old[1], await File.ReadAllBytesAsync(Path.Combine(root, targets[1])));
        Assert.Null(store.LoadJournal(out _));

        var currentState = CreateState(targets, next);
        currentState.SchemaVersion = 2;
        currentState.InstallationRoot = Path.GetFullPath(root);
        currentState.TargetLayout = WwmInstallationLayout.Package.ToString();
        currentState.InstallationFingerprint = WwmGameDefinition.ComputeInstallationFingerprint(root);
        await store.SaveAsync(currentState);
        for (var i = 0; i < 2; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), next[i]);
        var stateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WwmStateStore.SerializeState(currentState)))).ToLowerInvariant();
        PrepareJournal(store, root, old, next, targets, stateHash);
        var committedRecovery = await service.RecoverAsync(root, "42");
        Assert.True(committedRecovery.IsSuccess, committedRecovery.Message);
        Assert.Equal(next[0], await File.ReadAllBytesAsync(Path.Combine(root, targets[0])));
        Assert.NotNull(store.Load(out var stateError));
        Assert.Null(stateError);
        Assert.Null(store.LoadJournal(out _));
        Assert.NotNull(operation);
    }

    [Fact]
    public async Task Recover_UnknownExternalTargetBytesFailClosedAndRetainJournalAndBackups_WhenBuildIsUnknown()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var paths = WwmGameDefinition.Default.ManagedRelativePaths;
        var previous = new[] { new byte[] { 1, 2 }, new byte[] { 3, 4 } };
        var expected = new[] { new byte[] { 5, 6 }, new byte[] { 7, 8 } };
        var external = new byte[] { 90, 91, 92 };
        for (var i = 0; i < paths.Length; i++) File.WriteAllBytes(Path.Combine(root, paths[i]), previous[i]);
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        var journal = PrepareJournal(store, root, previous, expected, paths, "absent", gameBuildId: null);
        var target0 = Path.Combine(root, paths[0]);
        File.WriteAllBytes(target0, external);
        var logger = new NullLogger();
        var service = new WwmInstallService(new HttpClient(new StubHandler()), scope, store, logger);

        var result = await service.RecoverAsync(root, currentBuildId: null);

        Assert.Equal(WwmMutationError.RecoveryRequired, result.Error);
        Assert.Equal(external, await File.ReadAllBytesAsync(target0));
        Assert.Equal(previous[1], await File.ReadAllBytesAsync(Path.Combine(root, paths[1])));
        Assert.True(File.Exists(store.JournalFile));
        Assert.True(File.Exists(Path.Combine(store.TransactionDirectory, journal.OperationId, "0.bin")));
        Assert.Contains(logger.Errors, message => message.Contains(paths[0], StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recover_UnknownExternalRestoreTargetFailsClosedWithoutChangingEitherTarget()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var paths = WwmGameDefinition.Default.ManagedRelativePaths;
        var previous = new[] { new byte[] { 11, 12 }, new byte[] { 13, 14 } };
        var restored = new[] { new byte[] { 21, 22 }, new byte[] { 23, 24 } };
        var external = new byte[] { 99, 98 };
        for (var i = 0; i < paths.Length; i++) File.WriteAllBytes(Path.Combine(root, paths[i]), previous[i]);
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        var journal = PrepareJournal(store, root, previous, restored, paths, "absent", operation: "restore", gameBuildId: "42");
        var target0 = Path.Combine(root, paths[0]);
        File.WriteAllBytes(target0, external);
        var service = new WwmInstallService(new HttpClient(new StubHandler()), scope, store, Logger);

        var result = await service.RecoverAsync(root, "42");

        Assert.Equal(WwmMutationError.RecoveryRequired, result.Error);
        Assert.Equal(external, await File.ReadAllBytesAsync(target0));
        Assert.Equal(previous[1], await File.ReadAllBytesAsync(Path.Combine(root, paths[1])));
        Assert.True(File.Exists(store.JournalFile));
        Assert.True(File.Exists(Path.Combine(store.TransactionDirectory, journal.OperationId, "0.bin")));
    }

    [Fact]
    public async Task Recover_IncompleteTransactionWithChangedBuildFailsClosedWithoutWritingOldBytes()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var paths = WwmGameDefinition.Default.ManagedRelativePaths;
        var previous = new[] { new byte[] { 1, 3 }, new byte[] { 2, 4 } };
        var expected = new[] { new byte[] { 5, 7 }, new byte[] { 6, 8 } };
        for (var i = 0; i < paths.Length; i++) File.WriteAllBytes(Path.Combine(root, paths[i]), previous[i]);
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        var journal = PrepareJournal(store, root, previous, expected, paths, "absent", gameBuildId: "41");
        File.WriteAllBytes(Path.Combine(root, paths[0]), expected[0]);
        var service = new WwmInstallService(new HttpClient(new StubHandler()), scope, store, Logger);

        var result = await service.RecoverAsync(root, "42");

        Assert.Equal(WwmMutationError.SnapshotStale, result.Error);
        Assert.Equal(expected[0], await File.ReadAllBytesAsync(Path.Combine(root, paths[0])));
        Assert.Equal(previous[1], await File.ReadAllBytesAsync(Path.Combine(root, paths[1])));
        Assert.True(File.Exists(store.JournalFile));
        Assert.True(File.Exists(Path.Combine(store.TransactionDirectory, journal.OperationId, "0.bin")));
    }

    [Fact]
    public async Task Recover_CommittedTransactionWithChangedBuildCleansJournalWithoutRollback()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var paths = WwmGameDefinition.Default.ManagedRelativePaths;
        var previous = new[] { new byte[] { 1, 2 }, new byte[] { 3, 4 } };
        var installed = new[] { new byte[] { 5, 6 }, new byte[] { 7, 8 } };
        var state = CreateState(paths, installed);
        state.SchemaVersion = 2;
        state.InstallationRoot = Path.GetFullPath(root);
        state.TargetLayout = WwmInstallationLayout.Package.ToString();
        state.InstallationFingerprint = WwmGameDefinition.ComputeInstallationFingerprint(root);
        for (var i = 0; i < paths.Length; i++) File.WriteAllBytes(Path.Combine(root, paths[i]), installed[i]);
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        await store.SaveAsync(state);
        var stateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WwmStateStore.SerializeState(state)))).ToLowerInvariant();
        var journal = PrepareJournal(store, root, previous, installed, paths, stateHash, gameBuildId: "41");
        var tempFile = Path.Combine(root, paths[0]) + ".hub-" + journal.OperationId + ".tmp";
        await File.WriteAllBytesAsync(tempFile, new byte[] { 77 });
        var service = new WwmInstallService(new HttpClient(new StubHandler()), scope, store, Logger);

        var result = await service.RecoverAsync(root, "42");

        Assert.True(result.IsSuccess, result.Message);
        for (var i = 0; i < paths.Length; i++) Assert.Equal(installed[i], await File.ReadAllBytesAsync(Path.Combine(root, paths[i])));
        Assert.False(File.Exists(tempFile));
        Assert.Null(store.LoadJournal(out _));
        Assert.False(Directory.Exists(Path.Combine(store.TransactionDirectory, journal.OperationId)));
    }

    [Fact]
    public async Task Recover_CommittedRestoreWithChangedBuildRetiresConsumedSnapshot()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var paths = WwmGameDefinition.Default.ManagedRelativePaths;
        var beforeRestore = new[] { new byte[] { 31, 32 }, new byte[] { 33, 34 } };
        var restored = new[] { new byte[] { 41, 42 }, new byte[] { 43, 44 } };
        for (var i = 0; i < paths.Length; i++) File.WriteAllBytes(Path.Combine(root, paths[i]), restored[i]);
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        var journal = PrepareJournal(store, root, beforeRestore, restored, paths, "absent", operation: "restore", gameBuildId: "41");
        await CreatePreHubSnapshotAsync(store, paths, restored, "41", Guid.NewGuid().ToString("N"), root);
        var service = new WwmInstallService(new HttpClient(new StubHandler()), scope, store, Logger);

        var result = await service.RecoverAsync(root, "42");

        Assert.True(result.IsSuccess, result.Message);
        for (var i = 0; i < paths.Length; i++) Assert.Equal(restored[i], await File.ReadAllBytesAsync(Path.Combine(root, paths[i])));
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        Assert.Null(store.LoadJournal(out _));
        Assert.False(Directory.Exists(Path.Combine(store.TransactionDirectory, journal.OperationId)));
    }

    [Fact]
    public async Task ResolveState_DistinguishesCurrentDifferentModeAndExternalChangeCanBeReinstalled()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var archive = CreatePackage();
        var package = CreatePackageMetadata(archive);
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var paths = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        paths.EnsureDirectories();
        var store = new WwmStateStore(paths);
        var service = new WwmInstallService(client, paths, store, Logger);
        Assert.Equal(WwmInstalledStateKind.Unknown, (await service.ResolveStateAsync(root, package)).State);
        Assert.True((await service.InstallAsync(root, "42", package)).IsSuccess);
        Assert.Equal(WwmInstalledStateKind.Current, (await service.ResolveStateAsync(root, package)).State);
        var otherMode = new WwmApiPackage
        {
            Mode = new WwmMode { Slug = "english-items", Variant = "english_items" }, Version = package.Version,
            DownloadUrl = package.DownloadUrl, SizeBytes = package.SizeBytes, Sha256 = package.Sha256
        };
        Assert.Equal(WwmInstalledStateKind.UpdateAvailable, (await service.ResolveStateAsync(root, otherMode)).State);
        File.AppendAllText(Path.Combine(root, WwmGameDefinition.Default.ManagedRelativePaths[1]), "external");
        Assert.Equal(WwmInstalledStateKind.NeedsReinstall, (await service.ResolveStateAsync(root, package)).State);
        Assert.True((await service.InstallAsync(root, "42", package)).IsSuccess);
        Assert.Equal(WwmInstalledStateKind.Current, (await service.ResolveStateAsync(root, package)).State);
    }

    [Fact]
    public async Task InstallFailureAfterFirstReplacement_RestoresExactExternalTargetsAndPreviousState()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var paths = WwmGameDefinition.Default.ManagedRelativePaths;
        var packageArchive = CreatePackage();
        var package = CreatePackageMetadata(packageArchive);
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(packageArchive) });
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        var service = new WwmInstallService(client, scope, store, Logger);
        Assert.True((await service.InstallAsync(root, "42", package)).IsSuccess);
        var external = new[] { new byte[] { 101, 102, 103 }, new byte[] { 111, 112, 113, 114 } };
        for (var i = 0; i < paths.Length; i++) await File.WriteAllBytesAsync(Path.Combine(root, paths[i]), external[i]);
        var oldStateBytes = await File.ReadAllBytesAsync(store.StateFile);
        service.AfterTargetAppliedForTest = count => { if (count == 1) throw new IOException("synthetic second-target failure"); };

        var result = await service.InstallAsync(root, "42", package);

        Assert.Equal(WwmMutationError.Mutation, result.Error);
        for (var i = 0; i < paths.Length; i++) Assert.Equal(external[i], await File.ReadAllBytesAsync(Path.Combine(root, paths[i])));
        Assert.Equal(oldStateBytes, await File.ReadAllBytesAsync(store.StateFile));
        Assert.Null(store.LoadJournal(out _));
    }

    [Fact]
    public async Task InstallCancellationAfterFirstReplacement_RestoresExactExternalTargetsAndPreviousState()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var paths = WwmGameDefinition.Default.ManagedRelativePaths;
        var archive = CreatePackage();
        var package = CreatePackageMetadata(archive);
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        var service = new WwmInstallService(client, scope, store, Logger);
        Assert.True((await service.InstallAsync(root, "42", package)).IsSuccess);
        var external = new[] { new byte[] { 121, 122, 123 }, new byte[] { 131, 132, 133 } };
        for (var i = 0; i < paths.Length; i++) await File.WriteAllBytesAsync(Path.Combine(root, paths[i]), external[i]);
        var oldStateBytes = await File.ReadAllBytesAsync(store.StateFile);
        using var cancellation = new CancellationTokenSource();
        service.AfterTargetAppliedForTest = count => { if (count == 1) cancellation.Cancel(); };

        var result = await service.InstallAsync(root, "42", package, cancellation.Token);

        Assert.Equal(WwmMutationError.Cancelled, result.Error);
        for (var i = 0; i < paths.Length; i++) Assert.Equal(external[i], await File.ReadAllBytesAsync(Path.Combine(root, paths[i])));
        Assert.Equal(oldStateBytes, await File.ReadAllBytesAsync(store.StateFile));
        Assert.Null(store.LoadJournal(out _));
    }

    [Fact]
    public async Task Install_TargetChangesAfterRollbackCapture_FailsBeforeFirstReplacement()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var paths = WwmGameDefinition.Default.ManagedRelativePaths;
        var archive = CreatePackage();
        var package = CreatePackageMetadata(archive);
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        var service = new WwmInstallService(client, scope, store, Logger);
        var external = new[] { new byte[] { 141, 142, 143 }, new byte[] { 151, 152, 153 } };
        for (var i = 0; i < paths.Length; i++) await File.WriteAllBytesAsync(Path.Combine(root, paths[i]), external[i]);
        var changedAfterCapture = new byte[] { 161, 162, 163, 164 };
        service.BeforeTargetReplaceForTest = (relative, _) =>
        {
            if (relative == paths[0]) File.WriteAllBytes(Path.Combine(root, paths[1]), changedAfterCapture);
        };

        var result = await service.InstallAsync(root, "42", package);

        Assert.Equal(WwmMutationError.Mutation, result.Error);
        Assert.Equal(external[0], await File.ReadAllBytesAsync(Path.Combine(root, paths[0])));
        Assert.Equal(changedAfterCapture, await File.ReadAllBytesAsync(Path.Combine(root, paths[1])));
        Assert.False(File.Exists(store.StateFile));
        Assert.Null(store.LoadJournal(out _));
    }

    [Fact]
    public async Task Restore_BlocksCorruptedSnapshotAndChangedSteamBuildWithoutTouchingTargets()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var originals = new[] { new byte[] { 8, 9 }, new byte[] { 10, 11 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), originals[i]);
        var archive = CreatePackage();
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        var service = new WwmInstallService(client, scope, store, Logger);
        Assert.True((await service.InstallAsync(root, "42", CreatePackageMetadata(archive))).IsSuccess);
        var installed = targets.Select(path => File.ReadAllBytes(Path.Combine(root, path))).ToArray();
        var stale = await service.RestorePreHubAsync(root, "43");
        Assert.Equal(WwmMutationError.SnapshotStale, stale.Error);
        File.WriteAllBytes(Path.Combine(store.SnapshotDirectory, "0.bin"), new byte[] { 0 });
        var corrupted = await service.RestorePreHubAsync(root, "42");
        Assert.Equal(WwmMutationError.RecoveryRequired, corrupted.Error);
        for (var i = 0; i < targets.Length; i++)
            Assert.Equal(installed[i], await File.ReadAllBytesAsync(Path.Combine(root, targets[i])));
    }

    [Fact]
    public async Task Recover_MalformedJournalFailsClosedWithoutTouchingGame()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "Where Winds Meet");
        CreateGameMarkers(root);
        var targets = WwmGameDefinition.Default.ManagedRelativePaths;
        var bytes = new[] { new byte[] { 4 }, new byte[] { 5 } };
        for (var i = 0; i < targets.Length; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), bytes[i]);
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        Directory.CreateDirectory(store.TransactionDirectory);
        await File.WriteAllTextAsync(store.JournalFile, "{ bad-json");
        var service = new WwmInstallService(new HttpClient(new StubHandler()), scope, store, Logger);

        var result = await service.RecoverAsync(root, "42");

        Assert.Equal(WwmMutationError.RecoveryRequired, result.Error);
        for (var i = 0; i < targets.Length; i++) Assert.Equal(bytes[i], await File.ReadAllBytesAsync(Path.Combine(root, targets[i])));
    }

    [Fact]
    public async Task StateStore_RejectsJournalWithInvalidPreviousStatePayload()
    {
        using var temp = new TestTemp();
        var root = Path.Combine(temp.Root, "synthetic-game");
        CreateGameMarkers(root);
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        var paths = WwmGameDefinition.Default.ManagedRelativePaths;
        var bytes = new[] { new byte[] { 1 }, new byte[] { 2 } };
        var journal = PrepareJournal(store, root, bytes, bytes, paths, new string('a', 64));
        journal.PreviousStateExisted = true;
        journal.PreviousStateBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
        await store.WriteJournalAsync(journal);

        var loaded = store.LoadJournal(out var error);

        Assert.Null(loaded);
        Assert.Contains("state", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(store.JournalFile));
    }

    private static WwmTransactionJournal PrepareJournal(WwmStateStore store, string root, byte[][] previous, byte[][] expected,
        string[] paths, string stateHash, bool createdSnapshot = false, string operation = "install", string? gameBuildId = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(store.TransactionDirectory, id);
        Directory.CreateDirectory(dir);
        var priorEntries = new List<WwmPreviousTarget>();
        for (var i = 0; i < 2; i++)
        {
            File.WriteAllBytes(Path.Combine(dir, i + ".bin"), previous[i]);
            priorEntries.Add(new WwmPreviousTarget { RelativePath = paths[i], Existed = true, SizeBytes = previous[i].Length,
                Sha256 = Hash(previous[i]), BackupFile = i + ".bin" });
        }
        if (createdSnapshot)
            CreatePreHubSnapshotAsync(store, paths, previous, "42", id, root).GetAwaiter().GetResult();
        var journal = new WwmTransactionJournal
        {
            SchemaVersion = 2, OperationId = id, Operation = operation, CreatedPreHubSnapshot = createdSnapshot, PreviousTargets = priorEntries,
            TargetManifest = Enumerable.Range(0, 2).Select(i => new WwmTargetManifestEntry
                { RelativePath = paths[i], SizeBytes = expected[i].Length, Sha256 = Hash(expected[i]) }).ToList(),
            ExpectedStateSha256 = stateHash, GameBuildId = gameBuildId, InstallationRoot = Path.GetFullPath(root),
            TargetLayout = WwmInstallationLayout.Package.ToString(),
            InstallationFingerprint = WwmGameDefinition.ComputeInstallationFingerprint(root)
        };
        store.WriteJournalAsync(journal).GetAwaiter().GetResult();
        return journal;
    }

    private static async Task CreatePreHubSnapshotAsync(WwmStateStore store, string[] paths, byte[][] bytes, string buildId, string cycleId,
        string? installationRoot = null)
    {
        Directory.CreateDirectory(store.SnapshotDirectory);
        var targets = new List<WwmPreviousTarget>();
        for (var i = 0; i < paths.Length; i++)
        {
            var name = i + ".bin";
            await File.WriteAllBytesAsync(Path.Combine(store.SnapshotDirectory, name), bytes[i]);
            targets.Add(new WwmPreviousTarget
            {
                RelativePath = paths[i], Existed = true, SizeBytes = bytes[i].Length, Sha256 = Hash(bytes[i]), BackupFile = name
            });
        }
        var snapshot = new WwmPreHubSnapshot { CycleOperationId = cycleId, GameBuildId = buildId, Targets = targets };
        if (installationRoot != null)
        {
            snapshot.SchemaVersion = 2;
            snapshot.InstallationRoot = Path.GetFullPath(installationRoot);
            snapshot.TargetLayout = WwmInstallationLayout.Package.ToString();
            snapshot.InstallationFingerprint = WwmGameDefinition.ComputeInstallationFingerprint(installationRoot);
        }
        await File.WriteAllTextAsync(Path.Combine(store.SnapshotDirectory, "snapshot.json"),
            System.Text.Json.JsonSerializer.Serialize(snapshot));
    }

    private static async Task<LegacyAutoFixture> CreateLegacyAutoDetectedFixtureAsync(
        bool addSecondSteamRoot = false, string currentBuildId = "25281753", string stateBuildId = "25281753")
    {
        var temp = new TestTemp();
        try
        {
            var (steamRoot, root) = CreateSteamRoot(temp.Root, "steam-auto", "arbitrary-v1210-install", currentBuildId);
            var steamRoots = new List<string> { steamRoot };
            if (addSecondSteamRoot)
                steamRoots.Add(CreateSteamRoot(temp.Root, "steam-other", "other-valid-install").SteamRoot);
            var paths = new AppPaths(Path.Combine(temp.Root, "app"));
            var persistence = paths.GetGamePersistencePaths("where-winds-meet");
            var client = new HttpClient(new StubHandler());
            var session = WwmGameSession.CreateForTests(paths, Logger, client, ownsHttpClient: true, steamRoots: steamRoots);
            var targets = WwmGameDefinition.Default.GetManagedRelativePaths(WwmInstallationLayout.Package);
            var installed = new[] { new byte[] { 10, 11 }, new byte[] { 12, 13 } };
            var previous = new[] { new byte[] { 90, 91 }, new byte[] { 92, 93, 94 } };
            for (var index = 0; index < targets.Length; index++)
                await File.WriteAllBytesAsync(Path.Combine(root, targets[index]), installed[index]);
            persistence.EnsureDirectories();
            var store = session.StateStore;
            var cycleId = Guid.NewGuid().ToString("N");
            var archive = CreatePackage();
            var package = CreatePackageMetadata(archive);
            await store.SaveAsync(new WwmInstallationState
            {
                SchemaVersion = 1, ModeSlug = package.Mode.Slug, ModeVariant = package.Mode.Variant,
                ReleaseVersion = package.Version, ArchiveSizeBytes = package.SizeBytes, ArchiveSha256 = package.Sha256,
                PreHubSnapshotCycleId = cycleId, GameBuildId = stateBuildId, InstalledAt = DateTimeOffset.UtcNow,
                Targets = installed.Select((bytes, index) => new WwmTargetManifestEntry
                    { RelativePath = targets[index], SizeBytes = bytes.Length, Sha256 = Hash(bytes) }).ToList()
            });
            await CreatePreHubSnapshotAsync(store, targets, previous, "25281753", cycleId);
            return new LegacyAutoFixture(temp, session, root, targets, installed, package);
        }
        catch
        {
            temp.Dispose();
            throw;
        }
    }

    private static async Task<byte[][]> ReadTargetsAsync(string root, string[] targets)
    {
        var bytes = new byte[targets.Length][];
        for (var index = 0; index < targets.Length; index++)
            bytes[index] = await File.ReadAllBytesAsync(Path.Combine(root, targets[index]));
        return bytes;
    }

    private sealed class LegacyAutoFixture(TestTemp temp, WwmGameSession session, string root, string[] targets,
        byte[][] installed, WwmApiPackage package) : IDisposable
    {
        public WwmGameSession Session { get; } = session;
        public string Root { get; } = root;
        public string[] Targets { get; } = targets;
        public byte[][] Installed { get; } = installed;
        public WwmApiPackage Package { get; } = package;
        public void Dispose() { Session.Dispose(); temp.Dispose(); }
    }

    private static void AssertNoGameTemps(string root)
    {
        foreach (var localeRelative in new[]
        {
            WwmGameDefinition.Default.LocaleRelativePath,
            WwmGameDefinition.Default.LocalDataPatchLocaleRelativePath
        })
        {
            var locale = Path.Combine(root, localeRelative);
            if (!Directory.Exists(locale)) continue;
            Assert.Empty(Directory.EnumerateFiles(locale, "*.hub-*.tmp", SearchOption.TopDirectoryOnly));
            Assert.Empty(Directory.EnumerateFiles(locale, "*.rollback-*.tmp", SearchOption.TopDirectoryOnly));
        }
    }

    private static WwmInstallationState CreateState(string[] paths, byte[][] bytes) => new()
    {
        ModeSlug = "ukrainian", ModeVariant = "default", ReleaseVersion = "v-test", ArchiveSizeBytes = 2,
        ArchiveSha256 = new string('a', 64), PreHubSnapshotCycleId = Guid.NewGuid().ToString("N"), InstalledAt = DateTimeOffset.UtcNow,
        Targets = Enumerable.Range(0, 2).Select(i => new WwmTargetManifestEntry
            { RelativePath = paths[i], SizeBytes = bytes[i].Length, Sha256 = Hash(bytes[i]) }).ToList()
    };

    private static WwmReleaseFeed Feed(bool available)
    {
        var hash = new string('a', 64);
        return new WwmReleaseFeed { Success = true, Data = new WwmReleaseData
        {
            Current = new WwmCurrentRelease { Version = "v-test", Files = new List<WwmReleaseFile>
            {
                new() { Slug = "ukrainian", Variant = "default", DownloadUrl = "https://winds4ua.com.ua/package.zip", SizeBytes = 2, Sha256 = hash }
            }},
            Modes = new List<WwmMode>
            {
                new() { Slug = "ukrainian", Variant = "default", Label = "Українська", Available = available,
                    DownloadUrl = "https://winds4ua.com.ua/package.zip", SizeBytes = 2, Sha256 = hash }
            }
        }};
    }

    private static WwmApiPackage CreatePackageMetadata(byte[] archive) => new()
    {
        Mode = Feed(true).Data!.Modes![0], Version = "v-test", DownloadUrl = "https://winds4ua.com.ua/package.zip",
        SizeBytes = archive.Length, Sha256 = Hash(archive)
    };

    private static byte[] CreatePackage(params (string Path, byte[] Bytes)[] extras)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add("Package/HD/oversea/locale/translate_words_map_en", new byte[] { 10, 11 });
            Add("Package/HD/oversea/locale/translate_words_map_en_diff", new byte[] { 12, 13 });
            foreach (var extra in extras) Add(extra.Path, extra.Bytes);
            void Add(string path, byte[] bytes)
            {
                var entry = zip.CreateEntry(path);
                using var stream = entry.Open();
                stream.Write(bytes);
            }
        }
        return memory.ToArray();
    }

    private static byte[] CreatePackageWithContents(byte[] first, byte[] second)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in new[]
            {
                ("Package/HD/oversea/locale/translate_words_map_en", first),
                ("Package/HD/oversea/locale/translate_words_map_en_diff", second)
            })
            {
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(bytes);
            }
        }
        return memory.ToArray();
    }

    private static byte[] CreateIncompletePackage()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("Package/HD/oversea/locale/translate_words_map_en");
            using var stream = entry.Open();
            stream.Write(new byte[] { 10, 11 });
        }
        return memory.ToArray();
    }

    private static byte[] CreatePackageWithSymlink()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add("Package/HD/oversea/locale/translate_words_map_en", new byte[] { 10, 11 });
            Add("Package/HD/oversea/locale/translate_words_map_en_diff", new byte[] { 12, 13 });
            var link = zip.CreateEntry("Package/HD/oversea/locale/link");
            link.ExternalAttributes = unchecked((int)0xA0000000);
            using var stream = link.Open();
            stream.Write(Encoding.UTF8.GetBytes("target"));

            void Add(string path, byte[] bytes)
            {
                var entry = zip.CreateEntry(path);
                using var content = entry.Open();
                content.Write(bytes);
            }
        }
        return memory.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void CreateGameMarkers(string root)
        => CreateGameMarkers(root, localDataPatch: false);

    private static (string SteamRoot, string GameRoot) CreateSteamRoot(string parent, string steamDirectory, string installDirectory,
        string buildId = "25281753")
    {
        var steamRoot = Path.Combine(parent, steamDirectory);
        var gameRoot = Path.Combine(steamRoot, "steamapps", "common", installDirectory);
        CreateGameMarkers(gameRoot);
        var manifestPath = Path.Combine(steamRoot, "steamapps", "appmanifest_3564740.acf");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath,
            $"\"appid\" \"3564740\" \"installdir\" \"{installDirectory}\" \"buildid\" \"{buildId}\"");
        return (steamRoot, gameRoot);
    }

    private static void CreateGameMarkers(string root, bool localDataPatch)
    {
        Directory.CreateDirectory(root);
        var definition = WwmGameDefinition.Default;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, definition.ExecutableRelativePath))!);
        File.WriteAllBytes(Path.Combine(root, definition.ExecutableRelativePath), new byte[] { 1 });
        Directory.CreateDirectory(Path.Combine(root, localDataPatch
            ? definition.LocalDataPatchLocaleRelativePath
            : definition.LocaleRelativePath));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> response)
        => new(new StubHandler(response));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static readonly NullLogger Logger = new();

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
        public StubHandler() : this(_ => new HttpResponseMessage(HttpStatusCode.OK)) { }
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _response(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class DelayingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class NullLogger : ILogger
    {
        public List<string> Errors { get; } = new();
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) => Errors.Add(message);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Infos { get; } = new();
        public void Debug(string message) { }
        public void Info(string message) => Infos.Add(message);
        public void Warning(string message) { }
        public void Error(string message) { }
    }

    private sealed class TestTemp : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "wwm-stage10-tests", Guid.NewGuid().ToString("N"));
        public TestTemp() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            if (!Directory.Exists(Root)) return;
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
