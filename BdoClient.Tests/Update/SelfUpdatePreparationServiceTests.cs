using System.Diagnostics;
using System.Text;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class SelfUpdatePreparationServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppPaths _appPaths;
    private readonly NullLogger _logger = new();
    private readonly UpdateSessionStore _store;

    public SelfUpdatePreparationServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}");
        _appPaths = new AppPaths(_tempRoot);
        _appPaths.EnsureDirectories();
        _store = new UpdateSessionStore(_appPaths, _logger);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task PrepareAsync_SessionMissing_ReturnsSessionInvalid()
    {
        var service = CreateService("C:\\nonexistent.exe", "0.1.3", "0.1.4");
        var result = await service.PrepareAsync(Guid.NewGuid().ToString("D"));
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.SessionInvalid, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_SessionNotStaged_ReturnsSessionInvalid()
    {
        var session = MakeSession();
        session.State = "prepared";
        _store.WriteSession(session);

        var service = CreateService("C:\\nonexistent.exe", "0.1.3", "0.1.4");
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.SessionInvalid, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_StagedExeMissing_ReturnsStagedExeMissing()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var service = CreateService("C:\\nonexistent.exe", "0.1.3", "0.1.4");
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.StagedExeMissing, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_HashMismatch_ReturnsHashMismatch()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-WWM-UAClient.exe");
        File.WriteAllText(stagedExePath, "wrong content");

        var service = CreateService("C:\\nonexistent.exe", "0.1.3", "0.1.4");
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.HashMismatch, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_VersionMismatch_ReturnsVersionMismatch()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-WWM-UAClient.exe");
        File.WriteAllText(stagedExePath, "staged");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);
        _store.WriteSession(session);

        var service = CreateService(session.TargetPath, "0.1.3", "0.1.4",
            (path, tv, cv) => MakeVersionInfoWithVersion("9.9.9", "9.9.9"));
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.VersionMismatch, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_TargetMissing_ReturnsTargetMissing()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-WWM-UAClient.exe");
        File.WriteAllText(stagedExePath, "staged");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);
        _store.WriteSession(session);

        var service = CreateService(session.TargetPath, session.CurrentVersion, session.TargetVersion);
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.TargetMissing, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_ProcessPathMismatch_ReturnsTargetInvalid()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-WWM-UAClient.exe");
        File.WriteAllText(stagedExePath, "staged");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);

        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "target");

        _store.WriteSession(session);

        var service = CreateService("C:\\different-path\\BDO-WWM-UAClient.exe",
            session.CurrentVersion, session.TargetVersion);
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.TargetInvalid, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_BackupCollision_ReturnsBackupCollision()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-WWM-UAClient.exe");
        File.WriteAllText(stagedExePath, "new version");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);

        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "target");

        var backupPath = Workspace(session).BackupPath;
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "stale backup");

        _store.WriteSession(session);

        var service = CreateService(session.TargetPath, session.CurrentVersion, session.TargetVersion);
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.BackupCollision, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_CandidateCollision_ReturnsCandidateCollision()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-WWM-UAClient.exe");
        File.WriteAllText(stagedExePath, "staged");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);

        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "target");

        var candidatePath = Workspace(session).CandidatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);
        File.WriteAllText(candidatePath, "existing candidate");

        _store.WriteSession(session);

        var service = CreateService(session.TargetPath, session.CurrentVersion, session.TargetVersion);
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.CandidateCollision, result.Error);
        Assert.Equal("existing candidate", File.ReadAllText(candidatePath));
    }

    [Fact]
    public async Task PrepareAsync_CandidateCreateNew_AtomicFailure()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-WWM-UAClient.exe");
        File.WriteAllText(stagedExePath, "staged content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);

        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "target");

        var candidatePath = Workspace(session).CandidatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);
        File.WriteAllText(candidatePath, "raced candidate");

        _store.WriteSession(session);

        var service = CreateService(session.TargetPath, session.CurrentVersion, session.TargetVersion);
        var result = await service.PrepareAsync(session.SessionId);
        Assert.False(result.IsSuccess);
        Assert.Equal(SelfUpdatePreparationError.CandidateCollision, result.Error);
        Assert.Equal("raced candidate", File.ReadAllText(candidatePath));
    }

    [Fact]
    public async Task PrepareAsync_Success_CreatesCandidateAndMarksPrepared()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-WWM-UAClient.exe");
        File.WriteAllText(stagedExePath, "new version");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);

        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "old version");

        _store.WriteSession(session);

        var service = CreateService(session.TargetPath, session.CurrentVersion, session.TargetVersion);
        var result = await service.PrepareAsync(session.SessionId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Session);
        Assert.NotNull(result.CandidatePath);
        Assert.NotNull(result.OriginalExeSha256);

        Assert.True(File.Exists(result.CandidatePath));
        Assert.Equal("new version", File.ReadAllText(result.CandidatePath));

        Assert.Equal(UpdateSession.StatePrepared, result.Session.State);

        var loaded = _store.LoadSessionForState(session.SessionId, UpdateSession.StatePrepared);
        Assert.Equal(UpdateSessionLoadStatus.Valid, loaded.Status);
    }

    [Fact]
    public async Task PrepareAsync_OriginalExeSha256_MatchesTarget()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-WWM-UAClient.exe");
        File.WriteAllText(stagedExePath, "new");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);

        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "original content");
        var originalSha = await HashHelper.ComputeFileSha256Async(session.TargetPath);

        _store.WriteSession(session);

        var service = CreateService(session.TargetPath, session.CurrentVersion, session.TargetVersion);
        var result = await service.PrepareAsync(session.SessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalSha, result.OriginalExeSha256);
    }

    [Fact]
    public async Task PrepareAsync_CandidateInAppDataWorkspace()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-WWM-UAClient.exe");
        File.WriteAllText(stagedExePath, "new");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);

        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "old");

        _store.WriteSession(session);

        var service = CreateService(session.TargetPath, session.CurrentVersion, session.TargetVersion);
        var result = await service.PrepareAsync(session.SessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.GetDirectoryName(Workspace(session).CandidatePath), Path.GetDirectoryName(result.CandidatePath));
    }

    // --- §28 Preparation cancellation tests ---

    [Fact]
    public async Task PrepareAsync_CancellationAfterCreateNew_CleansUpCandidate()
    {
        var session = MakeSession();
        _store.WriteSession(session);

        var stagedDir = _store.GetSessionDir(session.SessionId);
        var stagedExePath = Path.Combine(stagedDir, "BDO-WWM-UAClient.exe");
        File.WriteAllText(stagedExePath, "staged content");
        session.StagedExeSha256 = await HashHelper.ComputeFileSha256Async(stagedExePath);

        var targetDir = Path.GetDirectoryName(session.TargetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(session.TargetPath, "target");

        var candidatePath = Workspace(session).CandidatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);

        _store.WriteSession(session);

        using var cts = new CancellationTokenSource();
        var service = CreateService(
            session.TargetPath,
            session.CurrentVersion,
            session.TargetVersion,
            copyFileCreateNew: (source, destination, token) =>
            {
                using var destinationStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                destinationStream.WriteByte(0x01);
                cts.Cancel();
                throw new OperationCanceledException(token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(session.SessionId, cts.Token));

        Assert.False(File.Exists(candidatePath));
        Assert.True(File.Exists(session.TargetPath));
        Assert.Equal("target", File.ReadAllText(session.TargetPath));
    }

    // --- Helpers ---

    private SelfUpdatePreparationService CreateService(
        string currentProcessPath,
        string currentVersion,
        string targetVersion,
        Func<string, string, string, FileVersionInfo>? versionInfoOverride = null,
        Func<string, string, CancellationToken, Task>? copyFileCreateNew = null)
    {
        FileVersionInfo VersionInfoFor(string path)
        {
            if (versionInfoOverride != null)
                return versionInfoOverride(path, targetVersion, currentVersion);

            var isTarget = string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(currentProcessPath),
                StringComparison.OrdinalIgnoreCase);

            var parsed = isTarget
                ? AppVersion.TryParseCoreVersion(currentVersion)
                : AppVersion.TryParseCoreVersion(targetVersion);

            if (!parsed.HasValue)
                return FileVersionInfo.GetVersionInfo(typeof(object).Assembly.Location);

            var fileVer = $"{parsed.Value.Major}.{parsed.Value.Minor}.{parsed.Value.Build}.0";
            var prodVer = $"{parsed.Value.Major}.{parsed.Value.Minor}.{parsed.Value.Build}";
            return MakeVersionInfoWithVersion(fileVer, prodVer);
        }

        return new SelfUpdatePreparationService(
            _store,
            _logger,
            () => currentProcessPath,
            VersionInfoFor,
            copyFileCreateNew);
    }

    private static FileVersionInfo MakeVersionInfoWithVersion(string fileVersion, string productVersion)
    {
        var info = FileVersionInfo.GetVersionInfo(typeof(object).Assembly.Location);
        typeof(FileVersionInfo)
            .GetField("_fileVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(info, fileVersion);
        typeof(FileVersionInfo)
            .GetField("_productVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(info, productVersion);
        return info;
    }

    private ReplacementWorkspace Workspace(UpdateSession session)
        => ReplacementWorkspace.Derive(_appPaths, session.SessionId, session.TargetPath);

    private static UpdateSession MakeSession() => new()
    {
        SchemaVersion = 1,
        SessionId = Guid.NewGuid().ToString("D"),
        CreatedAt = DateTimeOffset.UtcNow,
        State = "staged",
        CurrentVersion = "0.1.3",
        TargetVersion = "0.1.4",
        TargetTag = "v0.1.4",
        TargetPath = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}", "target", "BDO-WWM-UAClient.exe"),
        ParentPid = 12345,
        PackageAssetName = "BDO-WWM-UAClient.exe",
        PackageSha256 = new string('a', 64),
        StagedExeSha256 = new string('b', 64)
    };

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
