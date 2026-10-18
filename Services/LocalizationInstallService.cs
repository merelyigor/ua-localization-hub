using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Services;

public sealed class LocalizationInstallService
{
    private readonly LocalizationInstaller _installer;
    private readonly BackupStore _backupStore;
    private readonly InstallationStateStore _stateStore;
    private readonly ILogger _logger;
    private readonly string _gameRoot;
    private readonly BdoGameDefinition _gameDefinition;

    public LocalizationInstallService(
        LocalizationInstaller installer,
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger,
        string gameRoot,
        BdoGameDefinition? gameDefinition = null)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _backupStore = backupStore ?? throw new ArgumentNullException(nameof(backupStore));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gameRoot = gameRoot ?? throw new ArgumentNullException(nameof(gameRoot));
        _gameDefinition = gameDefinition ?? BdoGameDefinition.Default;
    }

    public async Task<InstallResult> InstallReleaseAsync(
        string modeSlug,
        CurrentRelease release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Info(
            $"Install transaction started: mode={modeSlug}, public_id={release.PublicId}, " +
            $"version={release.Version}, patch={release.Patch}, filename={release.Filename}, " +
            $"size={release.SizeBytes}, sha256={release.Sha256}, published_at={release.PublishedAt}.");

        // --- Phase 1: Input validation ---
        var gameLocFilePath = _gameDefinition.GetLocalizationFilePath(_gameRoot);

        if (!File.Exists(gameLocFilePath))
        {
            _logger.Error($"Game localization file not found: {gameLocFilePath}");
            return InstallResult.Failure(InstallError.InvalidGamePath,
                $"Game localization file not found: {gameLocFilePath}");
        }

        if (string.IsNullOrWhiteSpace(modeSlug))
            return InstallResult.Failure(InstallError.InvalidRelease, "modeSlug is empty");

        if (string.IsNullOrWhiteSpace(release.PublicId))
            return InstallResult.Failure(InstallError.InvalidRelease, "release.PublicId is empty");

        if (release.Version <= 0)
            return InstallResult.Failure(InstallError.InvalidRelease, "release.Version is invalid");

        if (release.Patch <= 0)
            return InstallResult.Failure(InstallError.InvalidRelease, "release.Patch is invalid");

        if (string.IsNullOrWhiteSpace(release.DownloadUrl)
            || !Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
            return InstallResult.Failure(InstallError.InvalidRelease, "release.DownloadUrl is invalid");

        if (release.SizeBytes <= 0)
            return InstallResult.Failure(InstallError.InvalidRelease, "release.SizeBytes is invalid");

        if (string.IsNullOrWhiteSpace(release.Sha256))
            return InstallResult.Failure(InstallError.InvalidRelease, "release.Sha256 is empty");

        if (!release.CompatibleWithOfficialPatch)
        {
            _logger.Error("Release incompatible: compatible_with_official_patch=false");
            return InstallResult.Failure(InstallError.Incompatible,
                "Release is not compatible with official patch");
        }

        // --- Phase 2: Validate pre-operation installation state ---
        var preStateLoad = _stateStore.Load();
        if (preStateLoad.Status == FileLoadStatus.Invalid)
        {
            _logger.Error($"Pre-operation installation state is invalid: {preStateLoad.Error}");
            return InstallResult.Failure(InstallError.PreOperationStateFailed,
                $"Pre-operation installation state is invalid: {preStateLoad.Error}");
        }

        var preStateBytes = _stateStore.CaptureRawState();

        // --- Phase 3: Download + Stage 4 verification ---
        DownloadResult downloadResult;
        try
        {
            downloadResult = await _installer
                .DownloadReleaseAsync(release, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            throw;
        }

        if (!downloadResult.IsSuccess)
        {
            _logger.Error($"Download failed: {downloadResult.Error}");
            CleanupDownloadTemp(downloadResult.TempFilePath);
            return InstallResult.Failure(InstallError.DownloadFailed,
                $"Download failed: {downloadResult.ErrorMessage}");
        }

        string downloadTempPath = downloadResult.TempFilePath!;

        // --- Phases 4-6: Pre-replace operations ---
        // Ownership flag: download temp is cleaned on ANY exit (exception, cancellation, early return)
        // except successful completion (preReplaceCompleted=true), where it's kept for ReplaceGameFileAsync.
        string? restorePointDir = null;
        bool preReplaceCompleted = false;
        try
        {
            // --- Phase 4: Original snapshot safety ---
            var (snapExists, snapValid, snapError) = await _backupStore
                .CheckOriginalSnapshotAsync(cancellationToken).ConfigureAwait(false);

            if (snapExists && !snapValid)
            {
                _logger.Error("Original snapshot corrupted — aborting transaction");
                return InstallResult.Failure(InstallError.OriginalSnapshotFailed,
                    "Original snapshot exists but is corrupted");
            }

            if (!snapExists)
            {
                if (preStateLoad.Status == FileLoadStatus.Valid && preStateLoad.Value?.Source == InstallationSource.Api)
                {
                    _logger.Error("No original snapshot but source=api metadata exists — aborting (inconsistent state)");
                    return InstallResult.Failure(InstallError.OriginalSnapshotFailed,
                        "Original snapshot missing with existing API installation metadata — inconsistent state");
                }

                _logger.Info("Creating original snapshot (first install)");
                var snapResult = await _backupStore
                    .CreateOriginalSnapshotAsync(_gameRoot, trustedGamePatch: null, cancellationToken)
                    .ConfigureAwait(false);

                if (!snapResult.IsSuccess)
                {
                    _logger.Error($"Failed to create original snapshot: {snapResult.Error}");
                    return InstallResult.Failure(InstallError.OriginalSnapshotFailed,
                        $"Failed to create original snapshot: {snapResult.ErrorMessage}");
                }
            }

            // --- Phase 5: Capture pre-operation state (after download, before restore point) ---
            preStateBytes = _stateStore.CaptureRawState();
            bool stateWasPresent = preStateBytes != null;

            // --- Phase 6: Create restore point (after verified download, before replace) ---
            if (File.Exists(gameLocFilePath))
            {
                var restorePointGamePatch = preStateLoad.Status == FileLoadStatus.Valid
                    ? preStateLoad.Value?.GamePatch
                    : null;

                var (rpDir, rpResult) = await _backupStore
                    .CreateRestorePointAsync(gameLocFilePath, restorePointGamePatch, "pre_install",
                        preStateBytes, stateWasPresent, cancellationToken)
                    .ConfigureAwait(false);

                if (!rpResult.IsSuccess || rpDir == null)
                {
                    _logger.Error($"Failed to create restore point: {rpResult.Error}");
                    return InstallResult.Failure(InstallError.BackupFailed,
                        $"Failed to create restore point: {rpResult.ErrorMessage}");
                }

                restorePointDir = rpDir;

                _logger.Info($"Restore point created: {rpDir}");
            }

            preReplaceCompleted = true;
        }
        catch (OperationCanceledException)
        {
            CleanupDownloadTemp(downloadTempPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Pre-replace phase failed: {ex.Message}");
            CleanupDownloadTemp(downloadTempPath);
            return InstallResult.Failure(InstallError.ReplaceFailed, ex.Message);
        }
        finally
        {
            if (!preReplaceCompleted)
                CleanupDownloadTemp(downloadTempPath);
        }

        // --- Phase 7: Replace game file (destructive boundary) ---
        try
        {
            var replaceResult = await _backupStore
                .ReplaceGameFileAsync(gameLocFilePath, downloadTempPath, restorePointDir!, cancellationToken)
                .ConfigureAwait(false);

            if (!replaceResult.IsSuccess)
            {
                CleanupDownloadTemp(downloadTempPath);

                if (replaceResult.Error == RestoreError.VerificationFailed)
                {
                    _logger.Error("Post-replace verification failed (internal recovery succeeded)");
                    return InstallResult.Failure(InstallError.VerificationFailed,
                        "Post-replace SHA-256 verification failed");
                }

                if (replaceResult.Error == RestoreError.RecoveryFailed)
                {
                    _logger.Error("Post-replace recovery failed — critical");
                    return InstallResult.Failure(InstallError.RollbackFailed,
                        "Replace failed and recovery also failed");
                }

                // ReplaceFailed without recovery (pre-replace failure)
                _logger.Error($"Replace failed: {replaceResult.Error}");
                return InstallResult.Failure(InstallError.ReplaceFailed,
                    replaceResult.ErrorMessage);
            }
        }
        catch (OperationCanceledException)
        {
            // ReplaceGameFileAsync handles post-replace recovery internally.
            // Pre-replace OCE: game untouched, state untouched.
            // Post-replace OCE: game recovered internally, state untouched.
            // Stage 6 does NOT perform second game rollback here.
            CleanupDownloadTemp(downloadTempPath);
            throw;
        }

        // --- Phase 8: Verify installed file against release contract ---
        try
        {
            var actualSize = new FileInfo(gameLocFilePath).Length;
            if (actualSize != release.SizeBytes)
            {
                _logger.Error($"Post-replace size mismatch: expected {release.SizeBytes}, got {actualSize}");
                CleanupDownloadTemp(downloadTempPath);
                var rollbackResult = await RollbackAsync(gameLocFilePath, restorePointDir, preStateBytes)
                    .ConfigureAwait(false);
                if (!rollbackResult.IsSuccess)
                    return InstallResult.Failure(InstallError.RollbackFailed, rollbackResult.ErrorMessage);
                return InstallResult.Failure(InstallError.VerificationFailed,
                    $"Post-replace size mismatch: expected {release.SizeBytes}, got {actualSize}");
            }

            var actualSha = await HashHelper.ComputeFileSha256Async(gameLocFilePath, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(actualSha, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error("Post-replace SHA-256 mismatch");
                CleanupDownloadTemp(downloadTempPath);
                var rollbackResult = await RollbackAsync(gameLocFilePath, restorePointDir, preStateBytes)
                    .ConfigureAwait(false);
                if (!rollbackResult.IsSuccess)
                    return InstallResult.Failure(InstallError.RollbackFailed, rollbackResult.ErrorMessage);
                return InstallResult.Failure(InstallError.VerificationFailed,
                    "Post-replace SHA-256 mismatch");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Transaction cancelled during verification — rolling back");
            CleanupDownloadTemp(downloadTempPath);
            var rollbackResult = await RollbackAsync(gameLocFilePath, restorePointDir, preStateBytes)
                .ConfigureAwait(false);
            if (!rollbackResult.IsSuccess)
                return InstallResult.Failure(InstallError.RollbackFailed, rollbackResult.ErrorMessage);
            throw;
        }

        // --- Phase 9: Save installation state ---
        var newMetadata = new InstallationMetadata
        {
            Source = InstallationSource.Api,
            ModeSlug = modeSlug,
            PublicId = release.PublicId,
            Version = release.Version,
            GamePatch = release.Patch,
            Sha256 = release.Sha256,
            InstalledAt = DateTimeOffset.UtcNow
        };

        try
        {
            await _stateStore.SaveAsync(newMetadata, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Transaction cancelled during state save — rolling back");
            CleanupDownloadTemp(downloadTempPath);
            var rollbackResult = await RollbackAsync(gameLocFilePath, restorePointDir, preStateBytes)
                .ConfigureAwait(false);
            if (!rollbackResult.IsSuccess)
                return InstallResult.Failure(InstallError.RollbackFailed, rollbackResult.ErrorMessage);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"State save failed: {ex.Message}");
            CleanupDownloadTemp(downloadTempPath);
            var rollbackResult = await RollbackAsync(gameLocFilePath, restorePointDir, preStateBytes)
                .ConfigureAwait(false);
            if (!rollbackResult.IsSuccess)
                return InstallResult.Failure(InstallError.RollbackFailed, rollbackResult.ErrorMessage);
            return InstallResult.Failure(InstallError.StateSaveFailed, ex.Message);
        }

        // --- Phase 10: Commit ---
        CleanupDownloadTemp(downloadTempPath);
        await _backupStore.PruneRestorePointsAsync(restorePointDir).ConfigureAwait(false);
        _logger.Info(
            $"Install transaction completed and state saved: mode={newMetadata.ModeSlug}, " +
            $"public_id={newMetadata.PublicId}, version={newMetadata.Version}, " +
            $"patch={newMetadata.GamePatch}, sha256={newMetadata.Sha256}.");
        return InstallResult.Success();
    }

    private async Task<RollbackResult> RollbackAsync(
        string gameLocFilePath, string? restorePointDir, byte[]? preStateBytes)
    {
        _logger.Warning("Rollback initiated");

        // 1. Restore game file
        var gameRestored = false;
        if (restorePointDir != null)
        {
            var rpFile = Path.Combine(restorePointDir, _gameDefinition.LocalizationFileName);
            if (File.Exists(rpFile))
            {
                try
                {
                    var recoveryResult = await _backupStore
                        .RecoverFromRestorePointAsync(gameLocFilePath, restorePointDir, CancellationToken.None)
                        .ConfigureAwait(false);
                    gameRestored = recoveryResult.IsSuccess;
                    if (gameRestored)
                        _logger.Info("Game file rollback succeeded");
                    else
                        _logger.Error($"Game file rollback failed: {recoveryResult.Error}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Game file rollback exception: {ex.Message}");
                }
            }
            else
            {
                _logger.Error("Restore point file missing — cannot rollback game file");
            }
        }
        else
        {
            _logger.Warning("No restore point — cannot rollback game file");
        }

        // 2. Restore installation state (always attempt, even if game rollback failed)
        var stateRestored = false;
        try
        {
            stateRestored = await RollbackInstallationStateAsync(preStateBytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"Installation state rollback exception: {ex.Message}");
        }

        if (gameRestored && stateRestored)
        {
            _logger.Info("Rollback completed: both components restored");
            return new RollbackResult(true, gameRestored, stateRestored, null);
        }

        _logger.Error($"Rollback partially failed: game={gameRestored}, state={stateRestored}");
        return new RollbackResult(false, gameRestored, stateRestored,
            $"Rollback partially failed: game={gameRestored}, state={stateRestored}");
    }

    private async Task<bool> RollbackInstallationStateAsync(byte[]? preStateBytes)
    {
        var stateWasPresent = File.Exists(_stateStore.InstallationFile);
        var stateRestored = await _stateStore
            .RestoreRawStateAsync(preStateBytes, CancellationToken.None)
            .ConfigureAwait(false);

        if (stateRestored && preStateBytes == null && stateWasPresent)
            _logger.Info("Installation state rollback: removed (was absent)");
        else if (stateRestored && preStateBytes != null)
            _logger.Info("Installation state rollback: restored from snapshot");
        else if (!stateRestored)
            _logger.Error(preStateBytes == null
                ? "Installation state rollback: file still exists after delete"
                : "Installation state rollback: verification mismatch after restore");

        return stateRestored;
    }

    private void CleanupDownloadTemp(string? tempPath)
    {
        if (tempPath == null) return;
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to cleanup download temp: {ex.Message}");
        }
    }

    private void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to cleanup temporary file {Path.GetFileName(path)}: {ex.Message}");
        }
    }
}

internal sealed class RollbackResult
{
    public bool IsSuccess { get; }
    public bool GameRestored { get; }
    public bool StateRestored { get; }
    public string? ErrorMessage { get; }

    public RollbackResult(bool isSuccess, bool gameRestored, bool stateRestored, string? errorMessage)
    {
        IsSuccess = isSuccess;
        GameRestored = gameRestored;
        StateRestored = stateRestored;
        ErrorMessage = errorMessage;
    }
}
