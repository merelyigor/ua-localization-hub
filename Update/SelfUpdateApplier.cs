using System.Diagnostics;
using BdoClient.Logging;
using BdoClient;
using BdoClient.Services;

namespace BdoClient.Update;

public sealed class SelfUpdateApplier
{
    private const int ParentWaitTimeoutSeconds = 30;
    private const int ParentPollIntervalMs = 250;
    private const int ReplaceRetryWindowMs = 60000;
    private const int ReplaceRetryInitialDelayMs = 100;
    private const int ReplaceRetryLaterDelayMs = 250;
    private const int ReplaceRetryMaxDelayMs = 500;
    private const int ReplaceRetryLogIntervalMs = 1000;

    private readonly UpdateSessionStore _sessionStore;
    private readonly ILogger _logger;
    private readonly Func<string> _getCurrentProcessPath;
    private readonly Func<string, FileVersionInfo> _getFileVersionInfo;
    private readonly Func<int, bool> _isProcessRunning;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly Action<string, string, string> _replaceFile;
    private readonly Func<long> _getTimestampMilliseconds;
    private readonly Action<int> _sleep;

    public SelfUpdateApplier(
        UpdateSessionStore sessionStore,
        ILogger logger)
        : this(sessionStore, logger,
            () => Environment.ProcessPath ?? "",
            path => FileVersionInfo.GetVersionInfo(path),
            pid => IsProcessRunningDefault(pid),
            psi => Process.Start(psi),
            (source, destination, backup) => File.Replace(source, destination, backup),
            () => Environment.TickCount64,
            Thread.Sleep)
    {
    }

    internal SelfUpdateApplier(
        UpdateSessionStore sessionStore,
        ILogger logger,
        Func<string> getCurrentProcessPath,
        Func<string, FileVersionInfo> getFileVersionInfo,
        Func<int, bool> isProcessRunning,
        Func<ProcessStartInfo, Process?> startProcess,
        Action<string, string, string>? replaceFile = null,
        Func<long>? getTimestampMilliseconds = null,
        Action<int>? sleep = null)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _getCurrentProcessPath = getCurrentProcessPath ?? throw new ArgumentNullException(nameof(getCurrentProcessPath));
        _getFileVersionInfo = getFileVersionInfo ?? throw new ArgumentNullException(nameof(getFileVersionInfo));
        _isProcessRunning = isProcessRunning ?? throw new ArgumentNullException(nameof(isProcessRunning));
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
        _replaceFile = replaceFile ?? ((source, destination, backup) => File.Replace(source, destination, backup));
        _getTimestampMilliseconds = getTimestampMilliseconds ?? (() => Environment.TickCount64);
        _sleep = sleep ?? Thread.Sleep;
    }

    public async Task<int> RunAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Self-update apply started (session={sessionId})");

        // 1. Load prepared session
        var loadResult = _sessionStore.LoadSessionForState(sessionId, UpdateSession.StatePrepared);
        if (loadResult.Status != UpdateSessionLoadStatus.Valid || loadResult.Session == null)
        {
            _logger.Error($"Self-update apply: session not valid (status={loadResult.Status})");
            return ExitCodeInvalidArgs;
        }

        var session = loadResult.Session;

        // 2. Verify helper identity: must run from staged EXE path
        var currentPath = _getCurrentProcessPath();
        if (string.IsNullOrWhiteSpace(currentPath) || !Path.IsPathRooted(currentPath))
        {
            _logger.Error("Self-update apply: cannot determine current executable path");
            return ExitCodeInvalidArgs;
        }

        var expectedHelperPath = Path.GetFullPath(Path.Combine(_sessionStore.GetSessionDir(sessionId), session.PackageAssetName));
        var actualHelperPath = Path.GetFullPath(currentPath);
        if (!string.Equals(actualHelperPath, expectedHelperPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Error($"Self-update apply: helper identity mismatch ({actualHelperPath} != {expectedHelperPath})");
            return ExitCodeInvalidArgs;
        }

        // 3. Verify helper SHA matches staged exe
        var helperSha = await HashHelper.ComputeFileSha256Async(currentPath, cancellationToken);
        if (!string.Equals(helperSha, session.StagedExeSha256, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update apply: helper SHA mismatch ({helperSha} != {session.StagedExeSha256})");
            return ExitCodeVerificationFailed;
        }

        // 4. Verify helper version metadata
        var targetVersion = AppVersion.TryParseCoreVersion(session.TargetVersion);
        if (!targetVersion.HasValue)
        {
            _logger.Error("Self-update apply: cannot parse target version");
            return ExitCodeInvalidArgs;
        }

        var expectedFileVersion = $"{targetVersion.Value.Major}.{targetVersion.Value.Minor}.{targetVersion.Value.Build}.0";
        var expectedProductVersion = $"{targetVersion.Value.Major}.{targetVersion.Value.Minor}.{targetVersion.Value.Build}";

        var helperVersionInfo = _getFileVersionInfo(currentPath);
        if (string.IsNullOrWhiteSpace(helperVersionInfo.FileVersion) ||
            string.IsNullOrWhiteSpace(helperVersionInfo.ProductVersion))
        {
            _logger.Error("Self-update apply: helper has no version metadata");
            return ExitCodeVerificationFailed;
        }

        if (!string.Equals(helperVersionInfo.FileVersion, expectedFileVersion, StringComparison.Ordinal) ||
            !string.Equals(helperVersionInfo.ProductVersion, expectedProductVersion, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update apply: helper version mismatch (FileVersion={helperVersionInfo.FileVersion}, ProductVersion={helperVersionInfo.ProductVersion})");
            return ExitCodeVerificationFailed;
        }

        // 5. Derive paths
        var targetPath = Path.GetFullPath(session.TargetPath);
        var targetDir = Path.GetDirectoryName(targetPath)!;
        var workspace = ReplacementWorkspace.Derive(_sessionStore.AppPaths, sessionId, targetPath);
        var candidatePath = workspace.CandidatePath;
        var backupPath = workspace.BackupPath;
        var failedNewPath = workspace.FailedNewPath;

        // 6. Verify candidate exists and hash matches
        if (!File.Exists(candidatePath))
        {
            _logger.Error($"Self-update apply: candidate not found at {candidatePath}");
            return ExitCodeVerificationFailed;
        }

        var candidateSha = await HashHelper.ComputeFileSha256Async(candidatePath, cancellationToken);
        if (!string.Equals(candidateSha, session.StagedExeSha256, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update apply: candidate SHA mismatch ({candidateSha} != {session.StagedExeSha256})");
            return ExitCodeVerificationFailed;
        }

        // 7. Wait for parent process to exit
        _logger.Info($"Self-update: waiting for parent PID {session.ParentPid} (timeout={ParentWaitTimeoutSeconds}s)");
        var parentExited = await WaitForParentExitAsync(session.ParentPid, cancellationToken);

        if (!parentExited)
        {
            _logger.Error($"Self-update apply: parent PID {session.ParentPid} did not exit within {ParentWaitTimeoutSeconds}s");
            return ExitCodeParentTimeout;
        }

        // 8. Final pre-replace revalidation (reload session from disk, canonical from here on)
        var reloadedResult = _sessionStore.LoadSessionForState(sessionId, UpdateSession.StatePrepared);
        if (reloadedResult.Status != UpdateSessionLoadStatus.Valid || reloadedResult.Session == null)
        {
            _logger.Error("Self-update apply: session invalid on reload before replace");
            return ExitCodeVerificationFailed;
        }

        var reloadedSession = reloadedResult.Session;

        // Compare critical immutable fields between initial and reloaded session
        if (!string.Equals(session.SessionId, reloadedSession.SessionId, StringComparison.Ordinal) ||
            !string.Equals(session.TargetPath, reloadedSession.TargetPath, StringComparison.Ordinal) ||
            !string.Equals(session.CurrentVersion, reloadedSession.CurrentVersion, StringComparison.Ordinal) ||
            !string.Equals(session.TargetVersion, reloadedSession.TargetVersion, StringComparison.Ordinal) ||
            !string.Equals(session.StagedExeSha256, reloadedSession.StagedExeSha256, StringComparison.Ordinal) ||
            !string.Equals(session.OriginalExeSha256, reloadedSession.OriginalExeSha256, StringComparison.Ordinal))
        {
            _logger.Error("Self-update apply: reloaded session differs from initial — failing closed");
            return ExitCodeVerificationFailed;
        }

        // Revalidate target
        if (!File.Exists(targetPath))
        {
            _logger.Error("Self-update apply: target missing before replace");
            return ExitCodeVerificationFailed;
        }

        var currentTargetSha = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken);
        if (!string.Equals(currentTargetSha, reloadedSession.OriginalExeSha256, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update apply: target changed after preparation ({currentTargetSha} != {reloadedSession.OriginalExeSha256})");
            return ExitCodeVerificationFailed;
        }

        var currentTargetVersion = _getFileVersionInfo(targetPath);
        var currentVersion = AppVersion.TryParseCoreVersion(reloadedSession.CurrentVersion);
        if (currentVersion.HasValue &&
            !string.IsNullOrWhiteSpace(currentTargetVersion.FileVersion) &&
            !string.IsNullOrWhiteSpace(currentTargetVersion.ProductVersion))
        {
            var expectedCurrentFileVersion = $"{currentVersion.Value.Major}.{currentVersion.Value.Minor}.{currentVersion.Value.Build}.0";
            var expectedCurrentProductVersion = $"{currentVersion.Value.Major}.{currentVersion.Value.Minor}.{currentVersion.Value.Build}";

            if (!string.Equals(currentTargetVersion.FileVersion, expectedCurrentFileVersion, StringComparison.Ordinal) ||
                !string.Equals(currentTargetVersion.ProductVersion, expectedCurrentProductVersion, StringComparison.Ordinal))
            {
                _logger.Error($"Self-update apply: target version changed after preparation");
                return ExitCodeVerificationFailed;
            }
        }

        // Revalidate candidate
        if (!File.Exists(candidatePath))
        {
            _logger.Error("Self-update apply: candidate missing before replace");
            return ExitCodeVerificationFailed;
        }

        var reloadedCandidateSha = await HashHelper.ComputeFileSha256Async(candidatePath, cancellationToken);
        if (!string.Equals(reloadedCandidateSha, reloadedSession.StagedExeSha256, StringComparison.Ordinal))
        {
            _logger.Error($"Self-update apply: candidate changed after preparation");
            return ExitCodeVerificationFailed;
        }

        // Revalidate backup must not exist
        if (File.Exists(backupPath))
        {
            _logger.Error($"Self-update apply: backup already exists at {backupPath}");
            return ExitCodeReplaceFailed;
        }

        // 9. Perform replacement using File.Replace
        try
        {
            ReplaceWithRetry(candidatePath, targetPath, backupPath, cancellationToken);
            _logger.Debug($"Self-update: File.Replace completed (candidate -> target, backup created)");
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update apply: File.Replace failed: {ex.Message}");
            await TryRecoverAsync(targetPath, backupPath, reloadedSession, failedNewPath, cancellationToken);
            return ExitCodeReplaceFailed;
        }

        // 10. Post-replace verification
        bool backupVerified = false;
        try
        {
            // Verify target
            if (!File.Exists(targetPath))
            {
                _logger.Error("Self-update apply: target missing after File.Replace");
                await TryRecoverAsync(targetPath, backupPath, reloadedSession, failedNewPath, cancellationToken);
                return ExitCodeVerificationFailed;
            }

            var replacedSha = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken);
            if (!string.Equals(replacedSha, reloadedSession.StagedExeSha256, StringComparison.Ordinal))
            {
                _logger.Error($"Self-update apply: replaced target SHA mismatch ({replacedSha} != {reloadedSession.StagedExeSha256})");
                await TryRecoverAsync(targetPath, backupPath, reloadedSession, failedNewPath, cancellationToken);
                return ExitCodeVerificationFailed;
            }

            var replacedVersionInfo = _getFileVersionInfo(targetPath);
            if (string.IsNullOrWhiteSpace(replacedVersionInfo.FileVersion) ||
                string.IsNullOrWhiteSpace(replacedVersionInfo.ProductVersion))
            {
                _logger.Error("Self-update apply: replaced target has no version metadata");
                await TryRecoverAsync(targetPath, backupPath, reloadedSession, failedNewPath, cancellationToken);
                return ExitCodeVerificationFailed;
            }

            if (!string.Equals(replacedVersionInfo.FileVersion, expectedFileVersion, StringComparison.Ordinal) ||
                !string.Equals(replacedVersionInfo.ProductVersion, expectedProductVersion, StringComparison.Ordinal))
            {
                _logger.Error($"Self-update apply: replaced target version mismatch (FileVersion={replacedVersionInfo.FileVersion}, ProductVersion={replacedVersionInfo.ProductVersion})");
                await TryRecoverAsync(targetPath, backupPath, reloadedSession, failedNewPath, cancellationToken);
                return ExitCodeVerificationFailed;
            }

            // Verify backup (§4 — degraded recovery if backup unavailable but target is good)
            backupVerified = false;
            if (!File.Exists(backupPath))
            {
                _logger.Error("Self-update apply: backup missing after File.Replace — backup recovery unavailable, continuing with verified new target");
            }
            else
            {
                var backupSha = await HashHelper.ComputeFileSha256Async(backupPath, cancellationToken);
                if (!string.Equals(backupSha, reloadedSession.OriginalExeSha256, StringComparison.Ordinal))
                {
                    _logger.Error($"Self-update apply: backup SHA mismatch after File.Replace ({backupSha} != {reloadedSession.OriginalExeSha256}) — backup unusable, continuing with verified new target");
                }
                else
                {
                    var backupVersionInfo = _getFileVersionInfo(backupPath);
                    if (currentVersion.HasValue &&
                        !string.IsNullOrWhiteSpace(backupVersionInfo.FileVersion) &&
                        !string.IsNullOrWhiteSpace(backupVersionInfo.ProductVersion))
                    {
                        var expectedBackupFileVersion = $"{currentVersion.Value.Major}.{currentVersion.Value.Minor}.{currentVersion.Value.Build}.0";
                        var expectedBackupProductVersion = $"{currentVersion.Value.Major}.{currentVersion.Value.Minor}.{currentVersion.Value.Build}";

                        if (!string.Equals(backupVersionInfo.FileVersion, expectedBackupFileVersion, StringComparison.Ordinal) ||
                            !string.Equals(backupVersionInfo.ProductVersion, expectedBackupProductVersion, StringComparison.Ordinal))
                        {
                            _logger.Error($"Self-update apply: backup version mismatch (FileVersion={backupVersionInfo.FileVersion}, ProductVersion={backupVersionInfo.ProductVersion}) — backup unusable, continuing with verified new target");
                        }
                        else
                        {
                            backupVerified = true;
                        }
                    }
                    else
                    {
                        backupVerified = true;
                    }
                }
            }

            if (!backupVerified)
                _logger.Warning("Self-update apply: proceeding in degraded recovery mode — rollback to old version will not be possible if restart fails");
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update apply: post-replace verification exception: {ex.Message}");
            await TryRecoverAsync(targetPath, backupPath, reloadedSession, failedNewPath, cancellationToken);
            return ExitCodeVerificationFailed;
        }

        // 11. Restart new EXE (BEFORE marking applied)
        _logger.Info($"Self-update: restarting new application at {targetPath}");
        Process? newProcess = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = false,
                WorkingDirectory = targetDir
            };
            newProcess = _startProcess(psi);
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update: restart new failed: {ex.Message}");
            if (!backupVerified)
            {
                _logger.Error("Self-update: cannot rollback — backup unavailable (degraded mode)");
                return ExitCodeRestartFailed;
            }
            var rollbackOk = await TryRollbackAsync(targetPath, backupPath, failedNewPath, reloadedSession, cancellationToken);
            if (rollbackOk)
            {
                _logger.Info("Self-update: rollback succeeded, attempting to restart restored old application");
                var oldRestarted = await TryRestartOldAsync(targetPath, targetDir, reloadedSession, cancellationToken);
                if (oldRestarted)
                {
                    _logger.Info("Self-update: restored old application restarted successfully (recovered from failed update)");
                    return ExitCodeRestartFailedRecovered;
                }
                _logger.Error("Self-update: critical — rollback succeeded but old restart failed");
            }
            return ExitCodeRestartFailed;
        }

        if (newProcess == null)
        {
            _logger.Error("Self-update: restart new returned null");
            if (!backupVerified)
            {
                _logger.Error("Self-update: cannot rollback — backup unavailable (degraded mode)");
                return ExitCodeRestartFailed;
            }
            var rollbackOk = await TryRollbackAsync(targetPath, backupPath, failedNewPath, reloadedSession, cancellationToken);
            if (rollbackOk)
            {
                _logger.Info("Self-update: rollback succeeded, attempting to restart restored old application");
                var oldRestarted = await TryRestartOldAsync(targetPath, targetDir, reloadedSession, cancellationToken);
                if (oldRestarted)
                {
                    _logger.Info("Self-update: restored old application restarted successfully (recovered from failed update)");
                    return ExitCodeRestartFailedRecovered;
                }
                _logger.Error("Self-update: critical — rollback succeeded but old restart failed");
            }
            return ExitCodeRestartFailed;
        }

        await ForegroundWindowHelper.TryBringToForegroundAsync(newProcess, _logger, cancellationToken);

        // 12. Mark session applied ONLY after successful restart — use reloaded session
        reloadedSession.State = UpdateSession.StateApplied;
        var writeResult = _sessionStore.WriteSession(reloadedSession);
        if (!writeResult.IsSuccess)
        {
            _logger.Warning("Self-update: failed to mark session as applied (process already started, continuing)");
        }

        _logger.Info("Self-update apply complete");
        return ExitCodeSuccess;
    }

    private void ReplaceWithRetry(string candidatePath, string targetPath, string backupPath, CancellationToken cancellationToken)
    {
        var startedAt = _getTimestampMilliseconds();
        var attempt = 0;
        var nextLogAt = ReplaceRetryLogIntervalMs;
        var waitedForLock = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _replaceFile(candidatePath, targetPath, backupPath);
                if (waitedForLock)
                    _logger.Info($"Self-update: File.Replace succeeded after transient lock wait of {GetElapsedMilliseconds(startedAt) / 1000.0:F1}s");
                return;
            }
            catch (IOException ex) when (IsTransientReplaceError(ex))
            {
                attempt++;
                waitedForLock = true;
                var elapsed = GetElapsedMilliseconds(startedAt);
                if (elapsed >= ReplaceRetryWindowMs)
                {
                    _logger.Error($"Self-update: File.Replace transient lock exhausted after {elapsed}ms: {ex.Message}");
                    throw;
                }

                if (elapsed >= nextLogAt)
                {
                    _logger.Warning($"Self-update: target still locked after {elapsed / 1000.0:F1}s");
                    nextLogAt += ReplaceRetryLogIntervalMs;
                }
                else if (attempt == 1)
                {
                    _logger.Warning($"Self-update: target temporarily locked; waiting for release (hresult=0x{ex.HResult:X8})");
                }

                var remaining = ReplaceRetryWindowMs - (int)Math.Min(ReplaceRetryWindowMs, elapsed);
                var delay = Math.Min(GetRetryDelayMilliseconds(attempt), remaining);
                _sleep(Math.Max(1, delay));
            }
        }
    }

    internal void ReplaceWithRetryForTests(string candidatePath, string targetPath, string backupPath, CancellationToken cancellationToken = default)
        => ReplaceWithRetry(candidatePath, targetPath, backupPath, cancellationToken);

    private static bool IsTransientReplaceError(IOException exception)
    {
        var win32Error = exception.HResult & 0xFFFF;
        return win32Error is 32 or 33;
    }

    private long GetElapsedMilliseconds(long startedAt)
        => Math.Max(0, _getTimestampMilliseconds() - startedAt);

    private static int GetRetryDelayMilliseconds(int attempt)
    {
        if (attempt <= 3)
            return ReplaceRetryInitialDelayMs;

        return Math.Min(ReplaceRetryMaxDelayMs,
            ReplaceRetryLaterDelayMs + (attempt - 4) * ReplaceRetryLaterDelayMs);
    }

    private async Task<bool> TryRollbackAsync(string targetPath, string backupPath, string failedNewPath, UpdateSession session, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(backupPath))
            {
                _logger.Error("Self-update: rollback failed — backup not found");
                return false;
            }

            // Verify backup SHA before using it (§4)
            var backupSha = await HashHelper.ComputeFileSha256Async(backupPath, cancellationToken);
            if (!string.Equals(backupSha, session.OriginalExeSha256, StringComparison.Ordinal))
            {
                _logger.Error($"Self-update: rollback aborted — backup SHA mismatch ({backupSha} != {session.OriginalExeSha256})");
                return false;
            }

            // Session-specific failed-new path (§5) — do NOT delete unexpected files
            if (File.Exists(failedNewPath))
            {
                _logger.Error($"Self-update: rollback failed — session-specific failed-new already exists at {failedNewPath}");
                return false;
            }

            if (File.Exists(targetPath))
            {
                // Replace failed new with backup, preserving old as .failed-new
                File.Replace(backupPath, targetPath, failedNewPath);
                _logger.Debug("Self-update: rollback via File.Replace completed");
            }
            else
            {
                // Target missing, move backup to target
                File.Move(backupPath, targetPath);
                _logger.Debug("Self-update: rollback via File.Move (target was missing)");
            }

            // Verify rollback
            if (File.Exists(targetPath))
            {
                var rollbackSha = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken);
                if (string.Equals(rollbackSha, session.OriginalExeSha256, StringComparison.Ordinal))
                {
                    _logger.Info("Self-update: rollback verified — original content restored");
                    return true;
                }
                else
                {
                    _logger.Error($"Self-update: rollback verification failed ({rollbackSha} != {session.OriginalExeSha256})");
                    return false;
                }
            }

            _logger.Error("Self-update: rollback failed — target missing after restore");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update: rollback failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TryRestartOldAsync(string targetPath, string targetDir, UpdateSession session, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(targetPath))
            {
                _logger.Error("Self-update: cannot restart old — target missing");
                return false;
            }

            var restoredSha = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken);
            if (!string.Equals(restoredSha, session.OriginalExeSha256, StringComparison.Ordinal))
            {
                _logger.Error($"Self-update: cannot restart old — restored SHA mismatch ({restoredSha} != {session.OriginalExeSha256})");
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = false,
                WorkingDirectory = targetDir
            };
            var oldProcess = _startProcess(psi);
            if (oldProcess == null)
            {
                _logger.Error("Self-update: restart old returned null");
                return false;
            }

            _logger.Info("Self-update: restored old application restarted");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update: restart old failed: {ex.Message}");
            return false;
        }
    }

    private async Task TryRecoverAsync(string targetPath, string backupPath, UpdateSession session, string failedNewPath, CancellationToken cancellationToken)
    {
        try
        {
            var targetExists = File.Exists(targetPath);
            var backupExists = File.Exists(backupPath);

            if (targetExists)
            {
                var currentSha = await HashHelper.ComputeFileSha256Async(targetPath, cancellationToken);
                if (string.Equals(currentSha, session.OriginalExeSha256, StringComparison.Ordinal))
                {
                    _logger.Info("Self-update: target still has original content — no recovery needed");
                    return;
                }
            }

            if (backupExists)
            {
                await TryRollbackAsync(targetPath, backupPath, failedNewPath, session, cancellationToken);
            }
            else
            {
                _logger.Error("Self-update: cannot recover — both target and backup missing/changed");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Self-update: recovery attempt failed: {ex.Message}");
        }
    }

    private async Task<bool> WaitForParentExitAsync(int parentPid, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(ParentWaitTimeoutSeconds);

        while (sw.Elapsed < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            if (!_isProcessRunning(parentPid))
            {
                _logger.Debug($"Self-update: parent PID {parentPid} exited after {sw.ElapsedMilliseconds}ms");
                return true;
            }

            try
            {
                await Task.Delay(ParentPollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        _logger.Warning($"Self-update: parent PID {parentPid} timeout after {timeout.TotalSeconds}s");
        return false;
    }

    private static bool IsProcessRunningDefault(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public const int ExitCodeSuccess = 0;
    public const int ExitCodeInvalidArgs = 1;
    public const int ExitCodeParentTimeout = 2;
    public const int ExitCodeVerificationFailed = 3;
    public const int ExitCodeReplaceFailed = 4;
    public const int ExitCodeRestartFailed = 5;
    public const int ExitCodeRestartFailedRecovered = 6;
}
