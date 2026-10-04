using System.Diagnostics;
using System.Windows.Forms;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient;

public partial class MainForm
{

    private async Task HandleInstallAsync()
    {
        if (_operationInProgress) return;

        string? finalMessage = null;

        try
        {
            _operationInProgress = true;
            _poller.Pause();
            _feedCoordinator.BlockUpdates();
            SetOperationState(OperationState.Idle);
            SetActionsEnabled(false);
            SetControlsDuringOperation(false);
            _operationCts = CancellationTokenSource.CreateLinkedTokenSource(
                _gameSessionCts?.Token ?? CancellationToken.None);
            var cancellationToken = _operationCts.Token;

            if (_gameRoot == null)
            {
                finalMessage = "Гру не знайдено.";
                return;
            }

            if (_releaseFeedSource != ReleaseFeedSource.Live)
            {
                finalMessage = "Встановлення та оновлення стануть доступні після відновлення з'єднання.";
                return;
            }

            var mode = GetSelectedApiMode();
            if (mode == null || string.IsNullOrWhiteSpace(mode.Slug))
            {
                finalMessage = "Не вдалося визначити режим локалізації.";
                return;
            }

            SetOperationState(OperationState.LoadingApi);
            var latest = await _apiClient.GetLatestReleaseAsync(mode.Slug, cancellationToken);
            if (_exitAfterOperation || _closing || cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            if (latest.Outcome is not (LatestReleaseOutcome.Modified or LatestReleaseOutcome.NotModified))
            {
                SetOperationState(latest.ErrorKind == ApiErrorKind.Cancelled
                    ? OperationState.Cancelled
                    : OperationState.Failed);
                _logger.Warning(
                    $"Install blocked by latest-release check: mode={mode.Slug}, outcome={latest.Outcome}, " +
                    $"error={latest.ErrorKind}, detail={latest.ErrorMessage}, " +
                    $"allowed={string.Join(",", latest.AllowedSlugs ?? Array.Empty<string>())}");
                finalMessage = latest.Outcome switch
                {
                    LatestReleaseOutcome.UnknownMode =>
                        "Режим локалізації більше не підтримується сервером. Оберіть інший режим або оновіть програму.",
                    LatestReleaseOutcome.PatchUnconfirmed =>
                        "Офіційний патч гри ще не підтверджено. Спробуйте пізніше.",
                    _ when latest.ErrorKind == ApiErrorKind.Cancelled => "Встановлення скасовано.",
                    _ => "Не вдалося перевірити актуальний реліз. Встановлення не виконано. Перевірте з'єднання та спробуйте ще раз."
                };
                return;
            }

            var current = latest.Data?.Current;
            if (current == null)
            {
                SetOperationState(OperationState.Failed);
                finalMessage = "Актуальний реліз для цього режиму зараз відсутній.";
                return;
            }

            SetOperationState(OperationState.Idle);

            // Keep fresh transaction metadata isolated from the aggregate feed and its UI cards.
            var transactionMode = new LocalizationMode
            {
                Slug = mode.Slug,
                PublicName = mode.PublicName,
                Description = mode.Description,
                Audience = mode.Audience,
                Current = current,
                History = mode.History
            };

            var compatResult = _compatService.Check(current);
            if (!compatResult.IsAllowed)
            {
                finalMessage = compatResult.Reason ?? "Операція заблокована.";
                return;
            }

            // Factual state check using INSTALLED mode current
            var installedLoad = _stateStore.Load();
            string? installedModeSlug = null;
            string? installedPublicId = null;
            CurrentRelease? installedModeCurrent = null;

            if (installedLoad.Status == FileLoadStatus.Valid && installedLoad.Value?.Source == InstallationSource.Api)
            {
                installedModeSlug = installedLoad.Value.ModeSlug;
                installedPublicId = installedLoad.Value.PublicId;
                if (string.Equals(installedModeSlug, mode.Slug, StringComparison.Ordinal))
                {
                    installedModeCurrent = current;
                }
                else
                {
                    var installedApiMode = _apiResponse?.Data?.Modes?
                        .FirstOrDefault(m => string.Equals(m.Slug, installedModeSlug, StringComparison.Ordinal));
                    installedModeCurrent = installedApiMode?.Current;
                }
            }

            var gameLocPath = _gameDefinition.GetLocalizationFilePath(_gameRoot);
            var factualState = await _stateService.ResolveAsync(
                installedModeCurrent, gameLocPath, cancellationToken, gameRoot: _gameRoot);

            // Do not cross the mutation boundary when shutdown/session cancellation
            // arrives while resolving the installed file's factual state.
            if (_exitAfterOperation || _closing || cancellationToken.IsCancellationRequested)
            {
                _logger.Info("Install aborted before transaction start because application shutdown is pending.");
                return;
            }

            var policy = InstallActionPolicy.Evaluate(
                factualState.State, installedModeSlug, installedPublicId,
                transactionMode, current, compatResult, operationInProgress: false);

            if (!policy.CanInstall)
            {
                if (policy.AlreadyInstalledExactTarget)
                    finalMessage = "Цей реліз уже встановлено.";
                else
                    finalMessage = "Встановлення недоступне для поточного стану.";
                return;
            }

            if (GameTestInstallPolicy.RequiresConfirmation(current.GameTest))
            {
                var confirmation = GameTestInstallPolicy.BuildConfirmationMessage(current.GameTest);
                var accepted = GameTestConfirmationForTest?.Invoke(current)
                    ?? MessageBox.Show(
                        this,
                        confirmation,
                        ApplicationBrand.DisplayName,
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                if (!accepted)
                {
                    SetOperationState(OperationState.Cancelled);
                    finalMessage = "Встановлення скасовано.";
                    return;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            SetMessage("Встановлення локалізації...");
            SetProgress(0);
            SetOperationState(OperationState.Downloading);

            cancelButton.Visible = true;
            cancelButton.Enabled = true;
            UpdateCancelButtonVisibility(_operationState);

            var service = new LocalizationInstallService(
                _localizationInstaller, _backupStore, _stateStore, _logger, _gameRoot, _gameDefinition);

            var progress = new Progress<DownloadProgress>(OnDownloadProgress);

            var result = await service.InstallReleaseAsync(
                transactionMode.Slug!, current, progress, cancellationToken);

            if (result.IsSuccess)
            {
                SetOperationState(OperationState.Completed);
                finalMessage = "Локалізацію успішно встановлено.";
            }
            else
            {
                SetOperationState(OperationState.Failed);
                _logger.Error($"Install failed: {result.Error} — {result.ErrorMessage}");
                var errorText = MapInstallError(result.Error!.Value);

                if (result.Error == InstallError.RollbackFailed)
                    finalMessage = $"КРИТИЧНО: {errorText}";
                else
                    finalMessage = errorText;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Install cancelled by user.");
            SetOperationState(OperationState.Cancelled);
            finalMessage = "Встановлення скасовано.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Install error: {ex.Message}");
            SetOperationState(OperationState.Failed);
            finalMessage = $"Помилка операції: {ex.Message}";
        }
        finally
        {
            cancelButton.Visible = false;
            cancelButton.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = null;
            _operationInProgress = false;
            SetControlsDuringOperation(true);

            try
            {
                try
                {
                    await RefreshStateAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Post-operation refresh failed: {ex.Message}");
                    if (finalMessage == null)
                        finalMessage = $"Не вдалося оновити стан: {ex.Message}";
                    else
                        finalMessage += $"{Environment.NewLine}{Environment.NewLine}Не вдалося оновити стан: {ex.Message}";
                }

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
    private async void RestoreOriginalButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await HandleRestoreOriginalAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"RestoreOriginalButton_Click unexpected: {ex.Message}");
            SetMessage($"Помилка: {ex.Message}");
        }
    }
    private async Task HandleRestoreOriginalAsync()
    {
        if (_operationInProgress) return;

        string? finalMessage = null;

        try
        {
            _operationInProgress = true;
            _poller.Pause();
            _feedCoordinator.BlockUpdates();
            SetOperationState(OperationState.Idle);
            SetActionsEnabled(false);
            SetControlsDuringOperation(false);

            if (_gameRoot == null)
            {
                finalMessage = "Гру не знайдено.";
                return;
            }

            if (_releaseFeedSource != ReleaseFeedSource.Live || _apiResponse?.Data == null)
            {
                finalMessage = "Відновлення оригіналу стане доступним після відновлення з'єднання.";
                return;
            }

            var data = _apiResponse.Data;
            var officialSourceUrl = data.OfficialSourceUrl;
            int? officialPatch = data.OfficialPatch > 0 ? data.OfficialPatch : null;

            SetMessage("Відновлення оригінального файлу...");
            SetProgress(0);
            SetOperationState(OperationState.Restoring);

            _operationCts = new CancellationTokenSource();
            cancelButton.Visible = true;
            cancelButton.Enabled = true;
            UpdateCancelButtonVisibility(_operationState);

            var service = new RestoreOriginalService(
                _localizationInstaller, _backupStore, _stateStore, _logger,
                _gameRoot, officialSourceUrl ?? "", officialPatch, _gameDefinition);

            var result = await service.RestoreOriginalAsync(_operationCts.Token);

            if (result.IsSuccess)
            {
                SetOperationState(OperationState.Completed);
                finalMessage = "Оригінальні файли відновлено.";
            }
            else
            {
                SetOperationState(OperationState.Failed);
                _logger.Error($"Restore original failed: {result.Error} — {result.ErrorMessage}");
                var errorText = MapRestoreError(result.Error!.Value);

                if (result.Error == RestoreError.RecoveryFailed)
                    finalMessage = $"КРИТИЧНО: {errorText}";
                else
                    finalMessage = errorText;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Restore Original cancelled by user.");
            SetOperationState(OperationState.Cancelled);
            finalMessage = "Відновлення оригіналу скасовано.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Restore original error: {ex.Message}");
            SetOperationState(OperationState.Failed);
            finalMessage = $"Помилка відновлення: {ex.Message}";
        }
        finally
        {
            cancelButton.Visible = false;
            cancelButton.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = null;
            _operationInProgress = false;
            SetControlsDuringOperation(true);

            try
            {
                try
                {
                    await RefreshStateAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Post-operation refresh failed: {ex.Message}");
                    if (finalMessage == null)
                        finalMessage = $"Не вдалося оновити стан: {ex.Message}";
                    else
                        finalMessage += $"{Environment.NewLine}{Environment.NewLine}Не вдалося оновити стан: {ex.Message}";
                }

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
    private void CancelButton_Click(object? sender, EventArgs e)
    {
        if (!_operationInProgress || _operationCts == null)
            return;

        cancelButton.Enabled = false;
        SetMessage("Скасування операції...");
        _operationCts.Cancel();
    }
    private static string MapInstallError(InstallError error) => error switch
    {
        InstallError.InvalidGamePath => "Шлях до гри недійсний або файл локалізації відсутній.",
        InstallError.InvalidRelease => "Метадані релізу пошкоджено або неповні.",
        InstallError.Incompatible => "Реліз не сумісний з поточним офіційним патчем гри.",
        InstallError.DownloadFailed => "Не вдалося завантажити файл локалізації. Перевірте з'єднання з Інтернетом.",
        InstallError.OriginalSnapshotFailed => "Не вдалося створити резервну копію оригінального файлу.",
        InstallError.PreOperationStateFailed => "Стан встановлення пошкоджено. Спробуйте перезапустити програму.",
        InstallError.BackupFailed => "Не вдалося створити точку відновлення.",
        InstallError.ReplaceFailed => "Не вдалося замінити файл локалізації у папці гри.",
        InstallError.VerificationFailed => "Перевірка встановленого файлу не пройдена. Файл може бути пошкоджено.",
        InstallError.StateSaveFailed => "Не вдалося зберегти стан встановлення. Зміни відкочено.",
        InstallError.RollbackFailed => "Не вдалося повністю відкотити зміни. Перевірте файли гри та журнал.",
        _ => "Невідома помилка встановлення."
    };
    private static string MapRestoreError(RestoreError error) => error switch
    {
        RestoreError.InvalidGamePath => "Шлях до гри недійсний або файл локалізації відсутній.",
        RestoreError.SourceMissing => "Вихідний файл відсутній.",
        RestoreError.SnapshotCorrupted => "Резервна копія пошкоджена.",
        RestoreError.BackupIo => "Не вдалося створити резервну копію поточного стану.",
        RestoreError.OfficialDownloadFailed => "Не вдалося завантажити оригінальний файл з сервера.",
        RestoreError.FallbackNotAllowed => "Відновлення з локальної копії неможливе (патч не збігається або копія відсутня).",
        RestoreError.PatchMismatch => "Патч локальної копії не збігається з поточним офіційним патчем.",
        RestoreError.ReplaceFailed => "Не вдалося замінити файл локалізації у папці гри.",
        RestoreError.VerificationFailed => "Перевірка відновленого файлу не пройдена.",
        RestoreError.StateSaveFailed => "Не вдалося зберегти стан встановлення після відновлення.",
        RestoreError.RecoveryFailed => "Не вдалося повністю відкотити зміни. Перевірте файли гри та журнал.",
        RestoreError.RestorePointNotFound => "Резервну копію не знайдено.",
        RestoreError.RestorePointInvalid => "Резервна копія пошкоджена або непридатна для відновлення.",
        RestoreError.StateRestoreFailed => "Не вдалося відновити стан локалізації. Попередній стан було повернуто.",
        _ => "Невідома помилка відновлення."
    };

}
