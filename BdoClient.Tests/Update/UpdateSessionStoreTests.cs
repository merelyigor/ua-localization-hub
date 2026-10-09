using System.Text.Json;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class UpdateSessionStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppPaths _appPaths;
    private readonly UpdateSessionStore _store;

    public UpdateSessionStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}");
        _appPaths = new AppPaths(_tempRoot);
        _appPaths.EnsureDirectories();
        _store = new UpdateSessionStore(_appPaths, new NullLogger());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void GetSessionDir_ValidGuid_ReturnsCorrectPath()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var dir = _store.GetSessionDir(sessionId);
        Assert.StartsWith(_appPaths.UpdatesDir, dir);
    }

    [Fact]
    public void GetSessionDir_InvalidGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => _store.GetSessionDir("not-a-guid"));
    }

    [Fact]
    public void GetSessionDir_PathEscape_Throws()
    {
        Assert.Throws<ArgumentException>(() => _store.GetSessionDir("../escape"));
    }

    [Fact]
    public void WriteSession_ThenLoad_Valid()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, result.Status);
        Assert.NotNull(result.Session);
        Assert.Equal(session.TargetVersion, result.Session!.TargetVersion);
    }

    [Fact]
    public void WriteSession_Atomic_NoTempAfterSuccess()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        Assert.Empty(Directory.GetFiles(sessionDir, "*.tmp"));
    }

    [Fact]
    public void LoadSession_Missing_ReturnsMissing()
    {
        var result = _store.LoadSession(Guid.NewGuid().ToString("D"));
        Assert.Equal(UpdateSessionLoadStatus.Missing, result.Status);
    }

    [Fact]
    public void LoadSession_MalformedJson_ReturnsInvalid()
    {
        var sessionId = Guid.NewGuid().ToString("D");
        var dir = _store.GetSessionDir(sessionId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "update-session.json"), "not json");
        var result = _store.LoadSession(sessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_WrongSchemaVersion_ReturnsInvalid()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var dir = _store.GetSessionDir(session.SessionId);
        var json = File.ReadAllText(Path.Combine(dir, "update-session.json"));
        json = json.Replace("\"schema_version\": 1", "\"schema_version\": 99");
        File.WriteAllText(Path.Combine(dir, "update-session.json"), json);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_WrongSessionId_ReturnsMissing()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var otherId = Guid.NewGuid().ToString("D");
        var result = _store.LoadSession(otherId);
        Assert.Equal(UpdateSessionLoadStatus.Missing, result.Status);
    }

    [Fact]
    public void LoadSession_SessionIdDiffersFromRequested_ReturnsInvalid()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var filePath = Path.Combine(sessionDir, "update-session.json");
        var json = File.ReadAllText(filePath);
        json = json.Replace(session.SessionId, Guid.NewGuid().ToString("D"));
        File.WriteAllText(filePath, json);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void CleanupSession_RemovesDirectory()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        Assert.True(Directory.Exists(sessionDir));
        Assert.True(_store.CleanupSession(session.SessionId));
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public void CleanupSession_Missing_DoesNotThrow()
    {
        Assert.Null(Record.Exception(() => _store.CleanupSession(Guid.NewGuid().ToString("D"))));
    }

    [Fact]
    public void CleanupSession_TransientHelperLock_RetriesAndSucceeds()
    {
        var session = MakeSession();
        session.PackageFileName = "BDO-UA-Client-v0.1.4-win-x64.zip";
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(sessionDir, session.PackageAssetName);
        File.WriteAllText(helperPath, "helper");
        File.WriteAllText(Path.Combine(sessionDir, session.PackageFileName), "package");
        session.StagedExeSha256 = HashHelper.ComputeFileSha256(helperPath);
        session.PackageSha256 = HashHelper.ComputeFileSha256(Path.Combine(sessionDir, session.PackageFileName));
        _store.WriteSession(session);

        var attempts = 0;
        _store.DeleteFileOverride = path =>
        {
            if (string.Equals(Path.GetFileName(path), session.PackageAssetName, StringComparison.OrdinalIgnoreCase)
                && ++attempts < 3)
                return false;

            File.Delete(path);
            return true;
        };

        Assert.True(_store.CleanupSession(session.SessionId));
        Assert.Equal(3, attempts);
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public void CleanupSession_PersistentHelperLock_RetainsRetryableMetadata()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(sessionDir, session.PackageAssetName);
        File.WriteAllText(helperPath, "helper");
        session.StagedExeSha256 = HashHelper.ComputeFileSha256(helperPath);
        _store.WriteSession(session);
        _store.DeleteFileOverride = path =>
            string.Equals(Path.GetFileName(path), session.PackageAssetName, StringComparison.OrdinalIgnoreCase)
                ? false
                : Delete(path);

        Assert.False(_store.CleanupSession(session.SessionId));
        Assert.True(File.Exists(Path.Combine(sessionDir, "update-session.json")));
        Assert.True(File.Exists(helperPath));
    }

    [Fact]
    public void CleanupSession_LaterRetry_RemovesPreviouslyLockedSession()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(sessionDir, session.PackageAssetName);
        File.WriteAllText(helperPath, "helper");
        session.StagedExeSha256 = HashHelper.ComputeFileSha256(helperPath);
        _store.WriteSession(session);

        _store.DeleteFileOverride = _ => false;
        Assert.False(_store.CleanupSession(session.SessionId));

        _store.DeleteFileOverride = Delete;
        Assert.True(_store.CleanupSession(session.SessionId));
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public void CleanupSession_UnknownFile_IsPreservedFailClosed()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var unknownPath = Path.Combine(sessionDir, "owner-data.bin");
        File.WriteAllText(unknownPath, "do not delete");

        Assert.False(_store.CleanupSession(session.SessionId));
        Assert.True(File.Exists(unknownPath));
        Assert.True(File.Exists(Path.Combine(sessionDir, "update-session.json")));
    }

    [Fact]
    public void LoadSession_LegacyWithoutPackageFileName_RemainsValid()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var filePath = Path.Combine(_store.GetSessionDir(session.SessionId), "update-session.json");
        var json = File.ReadAllText(filePath)
            .Replace("    \"package_file_name\": null,\r\n", string.Empty)
            .Replace("    \"package_file_name\": null,\n", string.Empty);
        File.WriteAllText(filePath, json);

        var result = _store.LoadSessionAnyState(session.SessionId);

        Assert.Equal(UpdateSessionLoadStatus.Valid, result.Status);
        Assert.Null(result.Session!.PackageFileName);
    }

    [Fact]
    public void CleanupSession_LegacyCanonicalPackageWithMatchingHashes_Succeeds()
    {
        var session = MakeSession();
        session.PackageFileName = null;
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(sessionDir, session.PackageAssetName);
        var packagePath = Path.Combine(sessionDir, "BDO-UA-Client-v0.1.4-win-x64.zip");
        File.WriteAllText(helperPath, "legacy helper");
        File.WriteAllText(packagePath, "legacy package");
        session.StagedExeSha256 = HashHelper.ComputeFileSha256(helperPath);
        session.PackageSha256 = HashHelper.ComputeFileSha256(packagePath);
        _store.WriteSession(session);
        RemovePackageFileNameFromJson(session);

        Assert.True(_store.CleanupSession(session.SessionId));
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public void CleanupSession_LegacyCanonicalPackageHashMismatch_RetainsSession()
    {
        var session = MakeSession();
        session.PackageFileName = null;
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(sessionDir, session.PackageAssetName);
        var packagePath = Path.Combine(sessionDir, "BDO-UA-Client-v0.1.4-win-x64.zip");
        File.WriteAllText(helperPath, "legacy helper");
        File.WriteAllText(packagePath, "legacy package");
        session.StagedExeSha256 = HashHelper.ComputeFileSha256(helperPath);
        session.PackageSha256 = new string('d', 64);
        _store.WriteSession(session);
        RemovePackageFileNameFromJson(session);

        Assert.False(_store.CleanupSession(session.SessionId));
        Assert.True(File.Exists(packagePath));
        Assert.True(File.Exists(Path.Combine(sessionDir, "update-session.json")));
    }

    [Fact]
    public void CleanupSession_CurrentPackageWithMatchingHash_Succeeds()
    {
        var session = MakeSession();
        session.PackageFileName = "verified-package.zip";
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(sessionDir, session.PackageAssetName);
        var packagePath = Path.Combine(sessionDir, session.PackageFileName);
        File.WriteAllText(helperPath, "current helper");
        File.WriteAllText(packagePath, "current package");
        session.StagedExeSha256 = HashHelper.ComputeFileSha256(helperPath);
        session.PackageSha256 = HashHelper.ComputeFileSha256(packagePath);
        _store.WriteSession(session);

        Assert.True(_store.CleanupSession(session.SessionId));
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public void CleanupSession_CurrentPackageHashMismatch_RetainsSession()
    {
        var session = MakeSession();
        session.PackageFileName = "verified-package.zip";
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(sessionDir, session.PackageAssetName);
        var packagePath = Path.Combine(sessionDir, session.PackageFileName);
        File.WriteAllText(helperPath, "current helper");
        File.WriteAllText(packagePath, "current package");
        session.StagedExeSha256 = HashHelper.ComputeFileSha256(helperPath);
        session.PackageSha256 = new string('d', 64);
        _store.WriteSession(session);

        Assert.False(_store.CleanupSession(session.SessionId));
        Assert.True(File.Exists(packagePath));
        Assert.True(File.Exists(Path.Combine(sessionDir, "update-session.json")));
    }

    [Fact]
    public void CleanupSession_HelperHashMismatch_RetainsSession()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var helperPath = Path.Combine(sessionDir, session.PackageAssetName);
        File.WriteAllText(helperPath, "unexpected helper");

        Assert.False(_store.CleanupSession(session.SessionId));
        Assert.True(File.Exists(helperPath));
        Assert.True(File.Exists(Path.Combine(sessionDir, "update-session.json")));
    }

    [Fact]
    public void UpdatesDir_CreatedByEnsureDirectories()
    {
        Assert.True(Directory.Exists(_appPaths.UpdatesDir));
    }

    [Fact]
    public void WriteSession_SnakeCaseJson()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var dir = _store.GetSessionDir(session.SessionId);
        var json = File.ReadAllText(Path.Combine(dir, "update-session.json"));
        Assert.Contains("schema_version", json);
        Assert.Contains("session_id", json);
        Assert.Contains("target_version", json);
        Assert.DoesNotContain("SchemaVersion", json);
    }

    [Fact]
    public void WriteSession_OverwriteExisting_Atomic()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        session.StagedExeSha256 = new string('c', 64);
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, result.Status);
        Assert.Equal(new string('c', 64), result.Session!.StagedExeSha256);
    }

    [Fact]
    public void WriteSession_ExistingNeverDisappears()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var filePath = Path.Combine(sessionDir, "update-session.json");
        var originalJson = File.ReadAllText(filePath);

        session.StagedExeSha256 = new string('c', 64);
        _store.WriteSession(session);

        var loaded = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, loaded.Status);
        Assert.Equal(new string('c', 64), loaded.Session!.StagedExeSha256);

        File.WriteAllText(filePath, "corrupted");
        session.StagedExeSha256 = new string('d', 64);
        var writeResult = _store.WriteSession(session);
        Assert.True(writeResult.IsSuccess);
        var afterCorruptRecovery = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, afterCorruptRecovery.Status);
        Assert.Equal(new string('d', 64), afterCorruptRecovery.Session!.StagedExeSha256);
    }

    // --- Canonical GUID (§5) ---

    [Fact]
    public void NormalizeSessionId_CanonicalDForm_Accepted()
    {
        var guid = Guid.NewGuid();
        var d = guid.ToString("D");
        var normalized = UpdateSessionStore.NormalizeSessionId(d);
        Assert.Equal(d, normalized);
    }

    [Fact]
    public void NormalizeSessionId_BraceFormat_Rejected()
    {
        var guid = Guid.NewGuid();
        var brace = guid.ToString("B");
        Assert.Throws<ArgumentException>(() => UpdateSessionStore.NormalizeSessionId(brace));
    }

    [Fact]
    public void NormalizeSessionId_NFormat_Rejected()
    {
        var guid = Guid.NewGuid();
        var n = guid.ToString("N");
        Assert.Throws<ArgumentException>(() => UpdateSessionStore.NormalizeSessionId(n));
    }

    [Fact]
    public void NormalizeSessionId_Empty_Rejected()
    {
        Assert.Throws<ArgumentException>(() => UpdateSessionStore.NormalizeSessionId(""));
    }

    [Fact]
    public void NormalizeSessionId_Null_Rejected()
    {
        Assert.Throws<ArgumentException>(() => UpdateSessionStore.NormalizeSessionId(null!));
    }

    [Fact]
    public void IsValidSessionId_CanonicalD_ReturnsTrue()
    {
        var id = Guid.NewGuid().ToString("D");
        Assert.True(UpdateSessionStore.IsValidSessionId(id));
    }

    [Fact]
    public void IsValidSessionId_BraceFormat_ReturnsFalse()
    {
        var id = Guid.NewGuid().ToString("B");
        Assert.False(UpdateSessionStore.IsValidSessionId(id));
    }

    [Fact]
    public void IsValidSessionId_NFormat_ReturnsFalse()
    {
        var id = Guid.NewGuid().ToString("N");
        Assert.False(UpdateSessionStore.IsValidSessionId(id));
    }

    [Fact]
    public void IsValidSessionId_Whitespace_ReturnsFalse()
    {
        var id = $" {Guid.NewGuid():D} ";
        Assert.False(UpdateSessionStore.IsValidSessionId(id));
    }

    [Fact]
    public void IsValidSessionId_BracesOnly_ReturnsFalse()
    {
        var guid = Guid.NewGuid();
        var id = $"{{{guid}}}";
        Assert.False(UpdateSessionStore.IsValidSessionId(id));
    }

    // --- SHA validation (§4) ---

    [Fact]
    public void IsValidSha256Hex_ValidLowerHex_ReturnsTrue()
    {
        Assert.True(UpdateSessionStore.IsValidSha256Hex(new string('a', 64)));
    }

    [Fact]
    public void IsValidSha256Hex_ValidUpperHex_ReturnsTrue()
    {
        Assert.True(UpdateSessionStore.IsValidSha256Hex(new string('A', 64)));
    }

    [Fact]
    public void IsValidSha256Hex_AllZ_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex(new string('z', 64)));
    }

    [Fact]
    public void IsValidSha256Hex_Whitespace_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex($" {new string('a', 64)} "));
    }

    [Fact]
    public void IsValidSha256Hex_PrefixSha256_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex("sha256:" + new string('a', 64)));
    }

    [Fact]
    public void IsValidSha256Hex_Short_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex(new string('a', 32)));
    }

    [Fact]
    public void IsValidSha256Hex_Long_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex(new string('a', 128)));
    }

    [Fact]
    public void IsValidSha256Hex_Null_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex(null));
    }

    [Fact]
    public void IsValidSha256Hex_Empty_ReturnsFalse()
    {
        Assert.False(UpdateSessionStore.IsValidSha256Hex(""));
    }

    // --- Session validation edge cases (§13) ---

    [Fact]
    public void LoadSession_InvalidState_ReturnsInvalid()
    {
        var session = MakeSession();
        session.State = "applied";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_MalformedCurrentVersion_ReturnsInvalid()
    {
        var session = MakeSession();
        session.CurrentVersion = "not-a-version";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_MalformedTargetVersion_ReturnsInvalid()
    {
        var session = MakeSession();
        session.TargetVersion = "not-a-version";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_TargetNotGreaterThanCurrent_ReturnsInvalid()
    {
        var session = MakeSession();
        session.TargetVersion = "0.1.3";
        session.TargetTag = "v0.1.3";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_TagMismatch_ReturnsInvalid()
    {
        var session = MakeSession();
        session.TargetTag = "v0.1.5";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_RelativeTargetPath_ReturnsInvalid()
    {
        var session = MakeSession();
        session.TargetPath = @"relative\path.exe";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_ParentPidZero_ReturnsInvalid()
    {
        var session = MakeSession();
        session.ParentPid = 0;
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_WrongAssetName_ReturnsInvalid()
    {
        var session = MakeSession();
        session.PackageAssetName = "wrong-name.zip";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Theory]
    [InlineData("BDO-WWM-UAClient.exe")]
    [InlineData("BDO-UA-Client.exe")]
    public void LoadSession_SupportedHelperIdentity_RemainsValid(string executableName)
    {
        var session = MakeSession();
        session.PackageAssetName = executableName;
        _store.WriteSession(session);

        var result = _store.LoadSession(session.SessionId);

        Assert.Equal(UpdateSessionLoadStatus.Valid, result.Status);
    }

    [Fact]
    public void LoadSession_PackageShaNonHex_ReturnsInvalid()
    {
        var session = MakeSession();
        session.PackageSha256 = new string('z', 64);
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_StagedExeShaNonHex_ReturnsInvalid()
    {
        var session = MakeSession();
        session.StagedExeSha256 = new string('z', 64);
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_ShortPackageSha_ReturnsInvalid()
    {
        var session = MakeSession();
        session.PackageSha256 = new string('a', 32);
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_LongStagedExeSha_ReturnsInvalid()
    {
        var session = MakeSession();
        session.StagedExeSha256 = new string('a', 128);
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSession_EmptyPackageSha_ReturnsInvalid()
    {
        var session = MakeSession();
        session.PackageSha256 = "";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    // --- WriteSession atomicity (§10) ---

    [Fact]
    public void WriteSession_TempDeletedAfterFailure()
    {
        var session = MakeSession();
        var result = _store.WriteSession(session);
        Assert.True(result.IsSuccess);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        Assert.Empty(Directory.GetFiles(sessionDir, "*.tmp"));
    }

    [Fact]
    public void WriteSession_ReplacesExistingSafely()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var dir = _store.GetSessionDir(session.SessionId);
        var filePath = Path.Combine(dir, "update-session.json");
        var firstContent = File.ReadAllText(filePath);

        session.StagedExeSha256 = new string('d', 64);
        _store.WriteSession(session);

        var secondContent = File.ReadAllText(filePath);
        Assert.NotEqual(firstContent, secondContent);
        Assert.Contains(new string('d', 64), secondContent);

        var loaded = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, loaded.Status);
    }

    // --- State lifecycle (§9) ---

    [Fact]
    public void LoadSessionForState_Prepared_AcceptsPreparedState()
    {
        var session = MakeSession();
        session.State = "prepared";
        session.OriginalExeSha256 = new string('c', 64);
        _store.WriteSession(session);
        var result = _store.LoadSessionForState(session.SessionId, "prepared");
        Assert.Equal(UpdateSessionLoadStatus.Valid, result.Status);
    }

    [Fact]
    public void LoadSessionForState_Staged_RejectsPreparedState()
    {
        var session = MakeSession();
        session.State = "prepared";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSessionForState_Prepared_RejectsStagedState()
    {
        var session = MakeSession();
        session.State = "staged";
        _store.WriteSession(session);
        var result = _store.LoadSessionForState(session.SessionId, "prepared");
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSessionForState_DefaultsToStaged()
    {
        var session = MakeSession();
        session.State = "staged";
        _store.WriteSession(session);
        var result = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, result.Status);
    }

    // --- original_exe_sha256 (§10) ---

    [Fact]
    public void OriginalExeSha256_Null_SerializedAsNull()
    {
        var session = MakeSession();
        session.OriginalExeSha256 = null;
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var json = File.ReadAllText(Path.Combine(sessionDir, "update-session.json"));
        Assert.Contains("original_exe_sha256", json);
        var loaded = _store.LoadSession(session.SessionId);
        Assert.Null(loaded.Session!.OriginalExeSha256);
    }

    [Fact]
    public void OriginalExeSha256_Present_RoundTrips()
    {
        var session = MakeSession();
        var sha = new string('c', 64);
        session.OriginalExeSha256 = sha;
        _store.WriteSession(session);
        var loaded = _store.LoadSession(session.SessionId);
        Assert.Equal(sha, loaded.Session!.OriginalExeSha256);
    }

    [Fact]
    public void OriginalExeSha256_OldSessionWithoutField_DefaultsToNull()
    {
        var session = MakeSession();
        _store.WriteSession(session);
        var sessionDir = _store.GetSessionDir(session.SessionId);
        var filePath = Path.Combine(sessionDir, "update-session.json");
        var json = File.ReadAllText(filePath);
        json = json.Replace("\"original_exe_sha256\": null,", "");
        File.WriteAllText(filePath, json);
        var loaded = _store.LoadSession(session.SessionId);
        Assert.Equal(UpdateSessionLoadStatus.Valid, loaded.Status);
        Assert.Null(loaded.Session!.OriginalExeSha256);
    }

    // --- Unsupported state rejection (§8) ---

    [Fact]
    public void LoadSessionForState_UnsupportedState_ReturnsInvalid()
    {
        var result = _store.LoadSessionForState(Guid.NewGuid().ToString("D"), "unknown_state");
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSessionForState_EmptyState_ReturnsInvalid()
    {
        var result = _store.LoadSessionForState(Guid.NewGuid().ToString("D"), "");
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    // --- State-specific original_exe_sha256 (§8) ---

    [Fact]
    public void LoadSessionForState_Prepared_MissingOriginalSha_ReturnsInvalid()
    {
        var session = MakeSession();
        session.State = "prepared";
        session.OriginalExeSha256 = null;
        _store.WriteSession(session);
        var result = _store.LoadSessionForState(session.SessionId, "prepared");
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSessionForState_Prepared_InvalidOriginalSha_ReturnsInvalid()
    {
        var session = MakeSession();
        session.State = "prepared";
        session.OriginalExeSha256 = "not-a-sha";
        _store.WriteSession(session);
        var result = _store.LoadSessionForState(session.SessionId, "prepared");
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSessionForState_Prepared_ValidOriginalSha_ReturnsValid()
    {
        var session = MakeSession();
        session.State = "prepared";
        session.OriginalExeSha256 = new string('c', 64);
        _store.WriteSession(session);
        var result = _store.LoadSessionForState(session.SessionId, "prepared");
        Assert.Equal(UpdateSessionLoadStatus.Valid, result.Status);
    }

    [Fact]
    public void LoadSessionForState_Applied_MissingOriginalSha_ReturnsInvalid()
    {
        var session = MakeSession();
        session.State = "applied";
        session.OriginalExeSha256 = null;
        _store.WriteSession(session);
        var result = _store.LoadSessionForState(session.SessionId, "applied");
        Assert.Equal(UpdateSessionLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadSessionForState_Applied_ValidOriginalSha_ReturnsValid()
    {
        var session = MakeSession();
        session.State = "applied";
        session.OriginalExeSha256 = new string('d', 64);
        _store.WriteSession(session);
        var result = _store.LoadSessionForState(session.SessionId, "applied");
        Assert.Equal(UpdateSessionLoadStatus.Valid, result.Status);
    }

    [Fact]
    public void LoadSessionForState_Staged_NullOriginalSha_ReturnsValid()
    {
        var session = MakeSession();
        session.State = "staged";
        session.OriginalExeSha256 = null;
        _store.WriteSession(session);
        var result = _store.LoadSessionForState(session.SessionId, "staged");
        Assert.Equal(UpdateSessionLoadStatus.Valid, result.Status);
    }

    private static UpdateSession MakeSession() => new()
    {
        SchemaVersion = 1,
        SessionId = Guid.NewGuid().ToString("D"),
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        State = "staged",
        CurrentVersion = "0.1.3",
        TargetVersion = "0.1.4",
        TargetTag = "v0.1.4",
        TargetPath = @"C:\test\BDO-UA-Client.exe",
        ParentPid = 12345,
        PackageAssetName = "BDO-UA-Client.exe",
        PackageSha256 = new string('a', 64),
        StagedExeSha256 = new string('b', 64)
    };

    private static bool Delete(string path)
    {
        File.Delete(path);
        return true;
    }

    private void RemovePackageFileNameFromJson(UpdateSession session)
    {
        var filePath = Path.Combine(_store.GetSessionDir(session.SessionId), "update-session.json");
        var json = File.ReadAllText(filePath)
            .Replace("    \"package_file_name\": null,\r\n", string.Empty)
            .Replace("    \"package_file_name\": null,\n", string.Empty);
        File.WriteAllText(filePath, json);
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
