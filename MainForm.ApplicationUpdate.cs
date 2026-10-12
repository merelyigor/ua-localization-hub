using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient;

public partial class MainForm
{
    // --- Application update (self-update) ---

    private void StartApplicationUpdateMonitoring()
    {
        versionLabel.Text = _appVersionInfo.DisplayVersion;

        if (!_appVersionInfo.IsPublicRelease)
        {
            _logger.Debug($"Update check skipped: not a public release ({_appVersionInfo.RawVersion})");
            return;
        }

        if (_updateCheckCts != null)
            return;

        _updateCheckCts = new CancellationTokenSource();
        _applicationUpdateTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)TimeSpan.FromMinutes(5).TotalMilliseconds
        };
        _applicationUpdateTimer.Tick += ApplicationUpdateTimer_Tick;
        _applicationUpdateTimer.Start();

        RequestApplicationUpdateCheck();
    }

    private void ApplicationUpdateTimer_Tick(object? sender, EventArgs e)
    {
        RequestApplicationUpdateCheck();
    }

    private void RequestApplicationUpdateCheck()
    {
        if (!_appVersionInfo.IsPublicRelease)
            return;
        if (_closing || _updateHandoffInProgress || IsDisposed || Disposing)
            return;
        if (_updateCheckCts == null || _updateCheckCts.IsCancellationRequested)
            return;
        if (_operationInProgress)
            return;
        if (_updateCheckTask != null && !_updateCheckTask.IsCompleted)
            return;

        _updateCheckTask = RunUpdateCheckAsync(_updateCheckCts.Token);
    }

    private async Task RunUpdateCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.Debug("Update check started");
            var result = await _gitHubClient.FetchReleasesAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested || _closing) return;

            if (!result.IsSuccess)
            {
                _logger.Warning($"Update check failed: {result.ErrorMessage}");
                return;
            }

            var candidate = _selectionPolicy.FindUpdate(_appVersionInfo, result.Value!);

            if (cancellationToken.IsCancellationRequested || _closing) return;

            ApplyApplicationUpdateResult(candidate, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.Error($"Update check error: {ex.Message}");
        }
    }

    private void ApplyApplicationUpdateResult(UpdateCandidate? candidate, CancellationToken cancellationToken)
    {
        if (InvokeRequired)
        {
            if (IsDisposed || Disposing || _closing || cancellationToken.IsCancellationRequested)
                return;

            BeginInvoke(new Action(() => ApplyApplicationUpdateResult(candidate, cancellationToken)));
            return;
        }

        if (cancellationToken.IsCancellationRequested || _closing || _updateHandoffInProgress)
            return;

        _pendingUpdateCandidate = candidate;
        if (candidate != null)
            _logger.Info($"Update available: {candidate.TagName}");
        else
            _logger.Debug("Update check: no eligible update");

        RefreshUpdateButtonPresentation();
        ObserveApplicationUpdateNotification(candidate?.TagName);
    }

    private void DisposeApplicationUpdateTimer()
    {
        if (_applicationUpdateTimer == null)
            return;

        _applicationUpdateTimer.Stop();
        _applicationUpdateTimer.Dispose();
        _applicationUpdateTimer = null;
    }

    private void RefreshUpdateButtonPresentation()
    {
        if (_stagedUpdateSession != null)
        {
            updateButton.Visible = false;
            updateButton.Enabled = false;
            return;
        }

        var state = UpdateButtonState.Compute(_pendingUpdateCandidate, IsUiWorkBlocked);
        updateButton.Text = state.Text;
        updateButton.Visible = state.Visible;
        updateButton.Enabled = state.Enabled;
        UiTheme.RefreshButtonState(updateButton);
    }

    private async void UpdateButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await HandleApplicationUpdateDownloadAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"UpdateButton_Click unexpected: {ex.Message}");
            SetMessage("Не вдалося виконати оновлення. Відкрийте папку журналів для деталей.");
        }
    }

    private async Task HandleApplicationUpdateDownloadAsync()
    {
        if (IsUiWorkBlocked) return;
        if (_pendingUpdateCandidate == null) return;

        string? finalMessage = null;

        try
        {
            _operationInProgress = true;
            _poller.Pause();
            _feedCoordinator.BlockUpdates();
            SetOperationState(OperationState.Idle);
            SetActionsEnabled(false);
            SetControlsDuringOperation(false);

            SetMessage($"Завантаження оновлення {_pendingUpdateCandidate.TagName}...");
            SetProgress(0);
            SetOperationState(OperationState.Downloading);

            _operationCts = new CancellationTokenSource();
            cancelButton.Visible = true;
            cancelButton.Enabled = true;
            UpdateCancelButtonVisibility(_operationState);

            var progress = new Progress<UpdateStageProgress>(stage =>
            {
                SetMessage(stage.Message);
                if (stage.Percent > 0)
                {
                    var clamped = (int)Math.Clamp(Math.Round(stage.Percent), 0, 100);
                    progressBar.Style = ProgressBarStyle.Continuous;
                    progressBar.Value = clamped;
                    progressLabel.Text = $"{clamped}%";
                }
            });

            var result = await _updatePackageService.StageUpdateAsync(
                _pendingUpdateCandidate, _appVersionInfo, progress, _operationCts.Token);

            if (result.IsSuccess)
            {
                SetOperationState(OperationState.Completed);
                _stagedUpdateSession = result.Session;

                // Prepare: copy candidate, capture original SHA, mark prepared
                SetMessage("Підготовка оновлення...");
                var prepResult = await _selfUpdatePreparation.PrepareAsync(result.Session!.SessionId, _operationCts.Token);

                if (!prepResult.IsSuccess)
                {
                    _logger.Error($"Self-update preparation failed: {prepResult.Error} — {prepResult.ErrorMessage}");
                    finalMessage = MapPreparationError(prepResult.Error!.Value);
                    _updateSessionStore.CleanupSession(result.Session!.SessionId);
                    _stagedUpdateSession = null;
                    return;
                }

                // Derive staged helper path from session store
                var stagedHelperPath = Path.Combine(
                    _updateSessionStore.GetSessionDir(result.Session!.SessionId),
                    ApplicationTechnicalIdentity.ExecutableFileName);

                if (!File.Exists(stagedHelperPath))
                {
                    _logger.Error($"Self-update handoff: staged helper not found at {stagedHelperPath}");
                    finalMessage = "Не вдалося знайти підготовлений файл оновлення.";
                    _updateSessionStore.CleanupSession(result.Session!.SessionId);
                    _stagedUpdateSession = null;
                    return;
                }

                // Disable cancel before handoff boundary
                cancelButton.Visible = false;
                cancelButton.Enabled = false;

                // Launch helper
                SetMessage("Підготовка оновлення... Програма буде перезапущена.");
                Process? helperProcess = null;
                try
                {
                    var stagedDir = Path.GetDirectoryName(stagedHelperPath)!;
                    var psi = new ProcessStartInfo
                    {
                        FileName = stagedHelperPath,
                        UseShellExecute = false,
                        WorkingDirectory = stagedDir
                    };
                    psi.ArgumentList.Add("--apply-update");
                    psi.ArgumentList.Add(result.Session!.SessionId);
                    helperProcess = Process.Start(psi);
                    _logger.Info($"Self-update: launched helper at {stagedHelperPath}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Self-update handoff: failed to launch helper: {ex.Message}");
                    finalMessage = "Не вдалося запустити процес оновлення.";
                    RestorePostHandoffFailureState();
                    return;
                }

                if (helperProcess == null)
                {
                    _logger.Error("Self-update handoff: Process.Start returned null");
                    finalMessage = "Не вдалося запустити процес оновлення.";
                    RestorePostHandoffFailureState();
                    return;
                }

                // Handoff successful — set flag and exit
                _updateHandoffInProgress = true;
                _poller.Stop();
                _updateCheckCts?.Cancel();
                _logger.Info("Self-update: exiting old process");
                Application.Exit();
            }
            else
            {
                if (result.Error == UpdatePackageError.Cancelled)
                    SetOperationState(OperationState.Cancelled);
                else
                    SetOperationState(OperationState.Failed);

                _logger.Error($"Update staging failed: {result.Error} — {result.ErrorMessage}");
                finalMessage = MapUpdatePackageError(result.Error!.Value);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Update staging cancelled by user.");
            CleanupAbandonedStagingSession();
            SetOperationState(OperationState.Cancelled);
            finalMessage = "Оновлення скасовано.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Update staging error: {ex.Message}");
            CleanupAbandonedStagingSession();
            SetOperationState(OperationState.Failed);
            finalMessage = "Не вдалося виконати оновлення. Відкрийте папку журналів для деталей.";
        }
        finally
        {
            cancelButton.Visible = false;
            cancelButton.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = null;
            _operationInProgress = false;

            if (!_updateHandoffInProgress)
            {
                SetControlsDuringOperation(true);

                try
                {
                    if (finalMessage != null)
                        SetMessage(finalMessage);

                    await _feedCoordinator.ApplyPendingIfAnyAsync();
                }
                finally
                {
                    _feedCoordinator.UnblockUpdates();
                    if (!_closing)
                        _poller.Resume();
                    CompletePendingExitAfterOperation();
                }
            }
        }
    }

    private static string MapUpdatePackageError(UpdatePackageError error) => error switch
    {
        UpdatePackageError.InvalidCandidate => "Неприйнятний кандидат оновлення.",
        UpdatePackageError.ManifestDownloadFailed => "Не вдалося отримати метадані оновлення. Спробуйте ще раз пізніше.",
        UpdatePackageError.ManifestInvalid => "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        UpdatePackageError.AssetMissing => "Не знайдено файл оновлення.",
        UpdatePackageError.DownloadFailed => "Не вдалося завантажити оновлення. Спробуйте ще раз пізніше.",
        UpdatePackageError.SizeMismatch => "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        UpdatePackageError.HashMismatch => "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        UpdatePackageError.PackageInvalid => "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        UpdatePackageError.ExecutableInvalid => "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        UpdatePackageError.SessionWriteFailed => "Не вдалося зберегти стан оновлення.",
        UpdatePackageError.IoError => "Помилка введення-виведення під час оновлення.",
        UpdatePackageError.Cancelled => "Оновлення скасовано.",
        _ => "Невідома помилка оновлення."
    };

    private static string MapPreparationError(SelfUpdatePreparationError error) => error switch
    {
        SelfUpdatePreparationError.WriteDenied =>
            "Не вдалося підготувати автоматичне оновлення, оскільки папка програми недоступна для запису.\nОновіть програму вручну або перемістіть її до папки, доступної для запису.",
        SelfUpdatePreparationError.CandidateCollision =>
            "Не вдалося підготувати оновлення: файл оновлення вже існує.\nСпробуйте ще раз або перезапустіть програму.",
        SelfUpdatePreparationError.BackupCollision =>
            "Не вдалося підготувати оновлення: резервна копія вже існує.\nСпробуйте ще раз або перезапустіть програму.",
        SelfUpdatePreparationError.CandidateCopyFailed =>
            "Не вдалося підготувати оновлення: помилка запису файлу.",
        SelfUpdatePreparationError.SessionWriteFailed =>
            "Не вдалося зберегти стан підготовки оновлення.",
        SelfUpdatePreparationError.HashMismatch =>
            "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        SelfUpdatePreparationError.VersionMismatch =>
            "Оновлення не пройшло перевірку версії. Поточна версія не змінена.",
        SelfUpdatePreparationError.StagedExeMissing =>
            "Не вдалося знайти підготовлений файл оновлення.",
        SelfUpdatePreparationError.TargetMissing =>
            "Поточний виконуваний файл не знайдено. Оновлення неможливе.",
        SelfUpdatePreparationError.TargetInvalid =>
            "Шлях до програми недійсний. Оновлення неможливе.",
        SelfUpdatePreparationError.SessionInvalid =>
            "Стан оновлення недійсний. Спробуйте ще раз.",
        _ => "Не вдалося підготувати оновлення."
    };

    private void RestorePostHandoffFailureState()
    {
        _logger.Info("Self-update: restoring state after pre-handoff failure");

        if (_stagedUpdateSession != null)
        {
            var session = _stagedUpdateSession;
            try
            {
                if (TryCleanupPreparedAttempt(session))
                {
                    _updateSessionStore.CleanupSession(session.SessionId);
                    _logger.Debug($"Self-update: cleaned up session {session.SessionId}");
                }
                else
                    _logger.Warning($"Self-update: retained session {session.SessionId} because candidate identity was not verified");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Self-update: pre-handoff cleanup failed (best-effort): {ex.Message}");
            }
        }

        _operationInProgress = false;
        _updateHandoffInProgress = false;
        _stagedUpdateSession = null;
        SetOperationState(OperationState.Idle);
        SetControlsDuringOperation(true);
        _feedCoordinator.UnblockUpdates();
        if (!_closing)
            _poller.Resume();
        RefreshUpdateButtonPresentation();
    }

    private bool TryCleanupPreparedAttempt(UpdateSession session)
    {
        return PreparedAttemptCleanup.TryDeleteCandidate(session, _appPaths, _logger);
    }

    private void CleanupAbandonedStagingSession()
    {
        if (_stagedUpdateSession == null)
            return;

        _updateSessionStore.CleanupSession(_stagedUpdateSession.SessionId);
        _stagedUpdateSession = null;
    }

}
