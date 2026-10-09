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
    public void Resolver_RejectsModeWithoutCorrespondingCurrentArtifact()
    {
        var feed = Feed(available: true);
        feed.Data!.Current!.Files!.Clear();
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
        var journal = PrepareJournal(store, previous, expected, targets, new string('a', 64));
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
        var journal = PrepareJournal(store, previous, expected, targets, new string('a', 64), createdSnapshot: true);
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
        await CreatePreHubSnapshotAsync(store, targets, baseline, "42", cycleId);
        var orphanOperationDir = Path.Combine(store.TransactionDirectory, cycleId);
        Directory.CreateDirectory(orphanOperationDir);
        await File.WriteAllBytesAsync(Path.Combine(orphanOperationDir, "0.bin"), new byte[] { 1 });
        var service = new WwmInstallService(new HttpClient(new StubHandler()), paths, store, Logger);

        var recovered = await service.RecoverAsync(root, "42");
        Assert.True(recovered.IsSuccess, recovered.Message);
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        Assert.False(Directory.Exists(orphanOperationDir));

        await CreatePreHubSnapshotAsync(store, targets, baseline, "42", Guid.NewGuid().ToString("N"));
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
        await CreatePreHubSnapshotAsync(store, targets, original, "42", snapshotCycleId);
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
        var operation = PrepareJournal(store, old, next, targets, stateHash: new string('a', 64));
        File.WriteAllBytes(Path.Combine(root, targets[0]), next[0]);

        var recovered = await service.RecoverAsync(root, "42");
        Assert.True(recovered.IsSuccess, recovered.Message);
        Assert.Equal(old[0], await File.ReadAllBytesAsync(Path.Combine(root, targets[0])));
        Assert.Equal(old[1], await File.ReadAllBytesAsync(Path.Combine(root, targets[1])));
        Assert.Null(store.LoadJournal(out _));

        var currentState = CreateState(targets, next);
        await store.SaveAsync(currentState);
        for (var i = 0; i < 2; i++) File.WriteAllBytes(Path.Combine(root, targets[i]), next[i]);
        var stateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WwmStateStore.SerializeState(currentState)))).ToLowerInvariant();
        PrepareJournal(store, old, next, targets, stateHash);
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
        var journal = PrepareJournal(store, previous, expected, paths, "absent", gameBuildId: null);
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
        var journal = PrepareJournal(store, previous, restored, paths, "absent", operation: "restore", gameBuildId: "42");
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
        var journal = PrepareJournal(store, previous, expected, paths, "absent", gameBuildId: "41");
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
        for (var i = 0; i < paths.Length; i++) File.WriteAllBytes(Path.Combine(root, paths[i]), installed[i]);
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        await store.SaveAsync(state);
        var stateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WwmStateStore.SerializeState(state)))).ToLowerInvariant();
        var journal = PrepareJournal(store, previous, installed, paths, stateHash, gameBuildId: "41");
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
        var journal = PrepareJournal(store, beforeRestore, restored, paths, "absent", operation: "restore", gameBuildId: "41");
        await CreatePreHubSnapshotAsync(store, paths, restored, "41", Guid.NewGuid().ToString("N"));
        var service = new WwmInstallService(new HttpClient(new StubHandler()), scope, store, Logger);

        var result = await service.RecoverAsync(root, "42");

        Assert.True(result.IsSuccess, result.Message);
        for (var i = 0; i < paths.Length; i++) Assert.Equal(restored[i], await File.ReadAllBytesAsync(Path.Combine(root, paths[i])));
        Assert.False(Directory.Exists(store.SnapshotDirectory));
        Assert.Null(store.LoadJournal(out _));
        Assert.False(Directory.Exists(Path.Combine(store.TransactionDirectory, journal.OperationId)));
    }

    [Fact]
    public async Task ResolveState_DistinguishesCurrentDifferentModeAndModifiedFiles()
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
        Assert.Equal(WwmInstalledStateKind.Modified, (await service.ResolveStateAsync(root, package)).State);
        Assert.Equal(WwmMutationError.ModifiedFiles, (await service.InstallAsync(root, "42", package)).Error);
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
        var scope = new AppPaths(Path.Combine(temp.Root, "app")).GetGamePersistencePaths("where-winds-meet");
        scope.EnsureDirectories();
        var store = new WwmStateStore(scope);
        var paths = WwmGameDefinition.Default.ManagedRelativePaths;
        var bytes = new[] { new byte[] { 1 }, new byte[] { 2 } };
        var journal = PrepareJournal(store, bytes, bytes, paths, new string('a', 64));
        journal.PreviousStateExisted = true;
        journal.PreviousStateBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
        await store.WriteJournalAsync(journal);

        var loaded = store.LoadJournal(out var error);

        Assert.Null(loaded);
        Assert.Contains("state", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(store.JournalFile));
    }

    private static WwmTransactionJournal PrepareJournal(WwmStateStore store, byte[][] previous, byte[][] expected,
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
            CreatePreHubSnapshotAsync(store, paths, previous, "42", id).GetAwaiter().GetResult();
        var journal = new WwmTransactionJournal
        {
            OperationId = id, Operation = operation, CreatedPreHubSnapshot = createdSnapshot, PreviousTargets = priorEntries,
            TargetManifest = Enumerable.Range(0, 2).Select(i => new WwmTargetManifestEntry
                { RelativePath = paths[i], SizeBytes = expected[i].Length, Sha256 = Hash(expected[i]) }).ToList(),
            ExpectedStateSha256 = stateHash, GameBuildId = gameBuildId
        };
        store.WriteJournalAsync(journal).GetAwaiter().GetResult();
        return journal;
    }

    private static async Task CreatePreHubSnapshotAsync(WwmStateStore store, string[] paths, byte[][] bytes, string buildId, string cycleId)
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
        await File.WriteAllTextAsync(Path.Combine(store.SnapshotDirectory, "snapshot.json"),
            System.Text.Json.JsonSerializer.Serialize(snapshot));
    }

    private static void AssertNoGameTemps(string root)
    {
        var locale = Path.Combine(root, WwmGameDefinition.Default.LocaleRelativePath);
        Assert.Empty(Directory.EnumerateFiles(locale, "*.hub-*.tmp", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(locale, "*.rollback-*.tmp", SearchOption.TopDirectoryOnly));
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
    {
        Directory.CreateDirectory(root);
        var definition = WwmGameDefinition.Default;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, definition.ExecutableRelativePath))!);
        File.WriteAllBytes(Path.Combine(root, definition.ExecutableRelativePath), new byte[] { 1 });
        Directory.CreateDirectory(Path.Combine(root, definition.LocaleRelativePath));
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
