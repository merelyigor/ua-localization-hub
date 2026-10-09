using System;
using System.Windows.Forms;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient;

public partial class MainForm
{
    // --- Local file-change monitor (T4) ---

    private System.Windows.Forms.Timer? _localFileMonitorTimer;
    private readonly LocalFileChangeTracker _localFileChangeTracker = new();
    private bool _localFileCheckInProgress;
    private FileSystemWatcher? _wwmFileWatcher;
    private System.Windows.Forms.Timer? _wwmFileRefreshTimer;
    private long _wwmWatchGeneration;

    private const int LocalFileMonitorIntervalMilliseconds = 300000; // ~5 minutes, hidden/background only

    private string? GetCurrentLocalizationFilePath()
        => _gameRoot == null ? null : _gameDefinition.GetLocalizationFilePath(_gameRoot);

    private bool HasValidApiManagedMetadata()
    {
        var load = _stateStore.Load();
        return load.Status == FileLoadStatus.Valid
            && load.Value?.Source == InstallationSource.Api;
    }

    /// <summary>
    /// Stops the periodic timer and forgets any committed baseline. Used when the game
    /// root becomes invalid so an old path baseline never survives into a no-game state.
    /// </summary>
    private void ClearLocalFileTracking()
    {
        _localFileMonitorTimer?.Stop();
        _localFileChangeTracker.Clear();
        DisposeWwmFileMonitor();
    }

    private void EnsureLocalFileMonitorTimer()
    {
        if (_localFileMonitorTimer != null)
            return;

        _localFileMonitorTimer = new System.Windows.Forms.Timer
        {
            Interval = LocalFileMonitorIntervalMilliseconds
        };
        _localFileMonitorTimer.Tick += LocalFileMonitorTimer_Tick;
    }

    /// <summary>
    /// Starts the monitor only when hidden/background, idle, and a baseline from a prior
    /// successful state resolution already exists. It never re-baselines from the current
    /// file, so it must not be used to "arm" a fresh baseline.
    /// </summary>
    private void StartLocalFileMonitorIfEligible()
    {
        if (_closing || IsDisposed || Disposing)
            return;

        // Negative eligibility: stop (but preserve the committed baseline) whenever the
        // monitor must not run. Operation/feed-blocked and baseline-absence are intentionally
        // NOT negative conditions — they are transient safety gates owned by the checker, so
        // the timer stays alive across hidden operations and background-startup recovery.
        if (Visible || _gameRoot == null || !HasValidApiManagedMetadata())
        {
            StopLocalFileMonitorPreservingBaseline();
            return;
        }

        var path = GetCurrentLocalizationFilePath();
        if (path == null)
        {
            StopLocalFileMonitorPreservingBaseline();
            return;
        }

        EnsureLocalFileMonitorTimer();
        if (_localFileMonitorTimer != null && !_localFileMonitorTimer.Enabled)
        {
            _localFileMonitorTimer.Start();
            _logger.Debug("Local file-change monitor started (hidden/background).");
        }
    }

    private void StopLocalFileMonitorPreservingBaseline()
    {
        if (_localFileMonitorTimer?.Enabled == true)
        {
            _localFileMonitorTimer.Stop();
            _logger.Debug("Local file-change monitor stopped (baseline preserved).");
        }
    }

    private void DisposeLocalFileMonitor()
    {
        if (_localFileMonitorTimer != null)
        {
            _localFileMonitorTimer.Stop();
            _localFileMonitorTimer.Tick -= LocalFileMonitorTimer_Tick;
            _localFileMonitorTimer.Dispose();
            _localFileMonitorTimer = null;
        }

        _localFileChangeTracker.Clear();
        DisposeWwmFileMonitor();
    }

    private void StartWwmFileMonitorIfEligible()
    {
        if (_wwmSession == null || _gameRoot == null || _closing || IsDisposed || Disposing) return;
        var directory = Path.Combine(_gameRoot, WwmGameDefinition.Default.LocaleRelativePath);
        if (!Directory.Exists(directory)) return;
        if (_wwmFileWatcher == null)
        {
            _wwmWatchGeneration = _gameSessionGeneration;
            _wwmFileRefreshTimer = new System.Windows.Forms.Timer { Interval = 700 };
            _wwmFileRefreshTimer.Tick += WwmFileRefreshTimer_Tick;
            _wwmFileWatcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _wwmFileWatcher.Changed += WwmFileWatcher_Changed;
            _wwmFileWatcher.Created += WwmFileWatcher_Changed;
            _wwmFileWatcher.Deleted += WwmFileWatcher_Changed;
            _wwmFileWatcher.Renamed += WwmFileWatcher_Renamed;
        }
    }

    private void WwmFileWatcher_Changed(object sender, FileSystemEventArgs e) => QueueWwmFileRefresh(e.FullPath);
    private void WwmFileWatcher_Renamed(object sender, RenamedEventArgs e) => QueueWwmFileRefresh(e.FullPath);

    private void QueueWwmFileRefresh(string path)
    {
        var expected = WwmGameDefinition.Default.ManagedRelativePaths
            .Select(relative => Path.GetFullPath(Path.Combine(_gameRoot ?? "", relative)));
        if (!expected.Contains(Path.GetFullPath(path), StringComparer.OrdinalIgnoreCase)
            || _closing || IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_wwmFileRefreshTimer == null || _wwmWatchGeneration != _gameSessionGeneration) return;
                _wwmFileRefreshTimer.Stop();
                _wwmFileRefreshTimer.Start();
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void WwmFileRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _wwmFileRefreshTimer?.Stop();
        if (_wwmSession == null || _operationInProgress || _wwmWatchGeneration != _gameSessionGeneration) return;
        var task = RefreshWwmStateAsync(_wwmWatchGeneration, _gameSessionCts?.Token ?? default);
        TrackSessionWork(task);
    }

    private void DisposeWwmFileMonitor()
    {
        if (_wwmFileWatcher != null)
        {
            _wwmFileWatcher.EnableRaisingEvents = false;
            _wwmFileWatcher.Changed -= WwmFileWatcher_Changed;
            _wwmFileWatcher.Created -= WwmFileWatcher_Changed;
            _wwmFileWatcher.Deleted -= WwmFileWatcher_Changed;
            _wwmFileWatcher.Renamed -= WwmFileWatcher_Renamed;
            _wwmFileWatcher.Dispose();
            _wwmFileWatcher = null;
        }
        if (_wwmFileRefreshTimer != null)
        {
            _wwmFileRefreshTimer.Stop();
            _wwmFileRefreshTimer.Tick -= WwmFileRefreshTimer_Tick;
            _wwmFileRefreshTimer.Dispose();
            _wwmFileRefreshTimer = null;
        }
    }

    private void LocalFileMonitorTimer_Tick(object? sender, EventArgs e)
    {
        if (_activeGameSession is not BdoGameSession bdo) return;
        var task = RunLocalFileCheckSafeAsync(allowVisible: false, _gameSessionGeneration, bdo,
            _gameSessionCts?.Token ?? default);
        TrackSessionWork(task);
    }

    /// <summary>
    /// Event-boundary wrapper for the fire-and-forget local-check Task. Catches any
    /// exception that escapes before the inner checker's own try/catch (e.g. a synchronous
    /// failure during pre-await eligibility/capture), preventing an unobserved Task fault.
    /// </summary>
    private async Task RunLocalFileCheckSafeAsync(
        bool allowVisible,
        long expectedGeneration,
        BdoGameSession expectedSession,
        CancellationToken cancellationToken)
    {
        try
        {
            await CheckLocalFileForChangesAsync(allowVisible, expectedGeneration, expectedSession, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning($"Local file change check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Shared local-change detection flow used by both the periodic timer (hidden only)
    /// and the restore check (allows visible). On change, it serializes against the API
    /// feed application via the existing FeedApplicationCoordinator so the two never run
    /// a state refresh concurrently.
    /// </summary>
    private async Task CheckLocalFileForChangesAsync(
        bool allowVisible,
        long expectedGeneration,
        BdoGameSession expectedSession,
        CancellationToken cancellationToken)
    {
        if (!IsCurrentGameSession(expectedGeneration, expectedSession))
            return;
        if (_localFileCheckInProgress)
            return;
        if (_closing || IsDisposed || Disposing)
            return;
        if (!allowVisible && Visible)
            return;
        if (_gameRoot == null)
            return;
        if (_operationInProgress)
            return;
        if (_feedCoordinator.IsBlocked)
            return;

        var path = GetCurrentLocalizationFilePath();
        if (path == null)
            return;

        if (_feedCoordinator.IsApplying)
        {
            _logger.Debug("Local file change check deferred: API feed application in progress.");
            return;
        }

        bool patchPresentationChanged = RefreshKnownGamePatchPresentation();
        bool hasValidMetadata = HasValidApiManagedMetadata();
        bool fileChanged = false;
        string? captureError = null;

        if (hasValidMetadata
            && LocalizationFileFingerprint.TryCapture(path, out var current, out captureError))
        {
            // With a committed baseline for this exact path, only a real fingerprint change
            // requires reconciliation. Without a baseline we cannot prove the current file
            // matches the displayed state, so one RefreshStateAsync establishes it. We never
            // adopt the current fingerprint silently; the baseline is committed only by the
            // existing RefreshStateAsync integration after a successful resolution.
            bool hasBaseline = _localFileChangeTracker.HasBaselineFor(path);
            if (hasBaseline)
            {
                fileChanged = _localFileChangeTracker.HasChanged(path, current);
                if (fileChanged)
                    _logger.Info("Localization file fingerprint changed; refreshing state.");
            }
            else
            {
                fileChanged = true;
                _logger.Info("Local file monitor baseline unavailable; refreshing state to establish it.");
            }
        }
        else if (hasValidMetadata && !patchPresentationChanged)
        {
            _logger.Warning($"Local file fingerprint capture failed: {captureError}");
            return;
        }

        if (!patchPresentationChanged && !fileChanged)
            return;

        _localFileCheckInProgress = true;
        try
        {
            if (_feedCoordinator.IsBlocked || _feedCoordinator.IsApplying)
            {
                _logger.Debug("Local file change check deferred: feed coordinator busy.");
                return;
            }

            _feedCoordinator.BlockUpdates();
            try
            {
                await RefreshStateAsync(expectedGeneration, cancellationToken);
                if (!IsCurrentGameSession(expectedGeneration, expectedSession))
                    return;
                // Re-read after the asynchronous state resolution so a newer local game
                // patch cannot be hidden by a refresh that started with an older value.
                if (RefreshKnownGamePatchPresentation())
                    await RefreshStateAsync(expectedGeneration, cancellationToken);
                await _feedCoordinator.ApplyPendingIfAnyAsync();
            }
            finally
            {
                _feedCoordinator.UnblockUpdates();
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Local file change state refresh failed: {ex.Message}");
        }
        finally
        {
            _localFileCheckInProgress = false;
        }
    }

    private bool RefreshKnownGamePatchPresentation()
    {
        if (_gameRoot == null)
            return false;

        var previousStatus = _gamePatchStatus;
        var previousText = gameStatusLabel.Text;
        var previousRefreshFailed = _gamePatchRefreshFailed;
        var installedPatch = _gameDefinition.TryReadInstalledPatch(_gameRoot);

        _gamePatchRefreshFailed = installedPatch is null;
        if (_gamePatchRefreshFailed)
            _logger.Warning("Local game patch refresh failed; localization write actions remain disabled.");

        ApplyGamePatchPresentation(_gameRoot, installedPatch);

        return previousStatus != _gamePatchStatus
            || !string.Equals(previousText, gameStatusLabel.Text, StringComparison.Ordinal)
            || previousRefreshFailed != _gamePatchRefreshFailed;
    }

    /// <summary>
    /// Schedules one cheap local-file comparison after tray restore. The form is already
    /// visible, so a change that occurred while hidden cannot stay stale until the next
    /// 5-minute monitor tick. Does not run during an active operation.
    /// </summary>
    private void ScheduleLocalFileCheckAfterRestore()
    {
        if (_operationInProgress || _closing || IsDisposed || Disposing)
            return;

        if (_activeGameSession is WwmGameSession)
        {
            TrackSessionWork(RefreshWwmStateAsync(_gameSessionGeneration, _gameSessionCts?.Token ?? default));
            return;
        }
        if (_activeGameSession is not BdoGameSession bdo)
            return;
        if (_feedCoordinator.IsBlocked)
            return;

        BeginInvoke(new Action(() =>
        {
            var task = RunLocalFileCheckSafeAsync(
                allowVisible: true,
                _gameSessionGeneration,
                bdo,
                _gameSessionCts?.Token ?? default);
            TrackSessionWork(task);
        }));
    }
}
