using BdoClient.Api;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient;

public partial class MainForm
{
    // --- Startup ---

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        _initializing = true;
        SetControlsDuringOperation(false);
        try
        {
            await ResolveSelectedGameAsync();
            await LoadCurrentGameSessionAsync(_gameSessionGeneration, _gameSessionCts!.Token, runGlobalStartup: true);
        }
        catch (Exception ex)
        {
            _logger.Error($"Startup error: {ex.Message}");
            SetMessage($"Помилка запуску: {ex.Message}");
        }
        finally
        {
            _startupTimer?.Stop();
            _startupTimer?.Dispose();
            _startupTimer = null;
            _initializing = false;
            SetOperationState(OperationState.Idle);
            SetControlsDuringOperation(true);
            if (!_closing && IsCurrentGameSession(_gameSessionGeneration, _activeGameSession))
                _poller.Start(_apiResponse);
            ScheduleContentFit();
            _startupCompletion.TrySetResult(null);
        }
    }

    private async Task LoadCurrentGameSessionAsync(
        long generation,
        CancellationToken cancellationToken,
        bool runGlobalStartup)
    {
        var configLoad = _configStore.Load();
        var config = configLoad.Value ?? new Config();

        SetOperationState(OperationState.LoadingApi);
        SetGameSearching();
        ShowModeLoadingPlaceholder();
        SetMessage("Завантаження даних з сервера...\nПерший запит може зайняти до 30 секунд. Будь ласка, зачекайте.");

        _startupStartTime = DateTime.Now;
        _startupTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _startupTimer.Tick += (s, args) =>
        {
            var elapsed = (int)(DateTime.Now - _startupStartTime).TotalSeconds;
            SetMessage($"Завантаження даних з сервера... ({elapsed} сек)\nПерший запит може зайняти до 30 секунд. Будь ласка, зачекайте.");
        };
        _startupTimer.Start();

        _ = _apiClient.WarmupConnectionAsync(cancellationToken);
        var coordinator = new StartupCoordinator(
            token => _apiClient.GetReleasesAsync(token),
            (patterns, token) => _gameDetector.DetectAsync(patterns, token),
            _logger);

        try
        {
            var result = await coordinator.RunAsync(
                onLocalDetectionComplete: localResult =>
                {
                    if (!IsCurrentGameSession(generation, _activeGameSession)) return;
                    if (localResult.GamePath != null)
                    {
                        _gameRoot = localResult.GamePath;
                        SetGameFound(localResult.GamePath, localResult.Source);
                    }
                    else
                        SetGameNotFound("Локально гру не знайдено. Очікування даних сервера...");
                },
                onApiComplete: apiResult =>
                {
                    if (!IsCurrentGameSession(generation, _activeGameSession)) return;
                    if (apiResult.Success && apiResult.Response != null)
                    {
                        _apiResponse = apiResult.Response;
                        _apiLoadedSuccessfully = true;
                        _releaseFeedSource = ReleaseFeedSource.Live;
                        _cachedFeedSavedAtUtc = null;
                        _apiErrorMessage = null;
                        _apiErrorKind = ApiErrorKind.None;
                        BuildDynamicModes();
                        RestoreInitialMode(config);
                    }
                    else
                    {
                        _apiLoadedSuccessfully = false;
                        _apiErrorKind = apiResult.ErrorKind;
                        _apiErrorMessage = apiResult.ErrorMessage;
                        ShowModeFailurePlaceholder();
                        SetMessage(ApiErrorPresentation.GetUserMessage(apiResult.ErrorKind, apiResult.ErrorMessage));
                    }
                },
                onFallbackStarted: () =>
                {
                    if (IsCurrentGameSession(generation, _activeGameSession))
                        SetGameNotFound("Пошук гри за даними сервера...");
                },
                cancellationToken: cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentGameSession(generation, _activeGameSession)) return;

            if (result.FinalGamePath != null)
            {
                _gameRoot = result.FinalGamePath;
                SetGameFound(result.FinalGamePath, result.FinalGameSource);
            }
            else
                SetGameNotFound("Гру не знайдено");

            if (result.ApiSuccess && result.ApiResponse != null)
                await PersistLiveFeedAsync(result.ApiResponse);
            else
                TryApplyCachedFeed();

            await RefreshStateAsync(generation, cancellationToken);
        }
        finally
        {
            _startupTimer?.Stop();
            _startupTimer?.Dispose();
            _startupTimer = null;
            if (runGlobalStartup && !_closing && IsCurrentGameSession(generation, _activeGameSession))
            {
                await StartupUpdateLifecycleCoordinator.RunAsync(
                    RunStartupLifecycleMaintenanceAsync,
                    () => _closing,
                    StartApplicationUpdateMonitoring,
                    ex => _logger.Warning($"Startup lifecycle maintenance failed: {ex.Message}"));
            }
        }
    }

    private Task ResolveSelectedGameAsync()
    {
        var load = _applicationConfigStore.Load();
        if (load.Status == FileLoadStatus.Invalid)
        {
            _logger.Warning("Application config invalid; using default game selection.");
            return Task.CompletedTask;
        }

        var config = load.Value ?? new ApplicationConfig();
        var requestedId = config.SelectedGameId;
        var selectedGame = _gameCatalog.Resolve(requestedId);
        var knownSelection = !string.IsNullOrWhiteSpace(requestedId)
            && _gameCatalog.Games.Any(game =>
                string.Equals(game.Id, requestedId, StringComparison.OrdinalIgnoreCase));

        if (knownSelection
            && !string.Equals(selectedGame.Id, _activeGameSession.Descriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            ActivateInitialGameSession(selectedGame);
        }

        _selectedGame = _activeGameSession.Descriptor;
        RestoreGameSelectorToActiveSession();

        if (!knownSelection)
        {
            if (!string.IsNullOrWhiteSpace(requestedId))
                _logger.Warning($"Unknown selected game '{requestedId}'; using default game '{_selectedGame.Id}'.");

            config.SelectedGameId = _selectedGame.Id;
            try
            {
                _applicationConfigStore.SaveAsync(config).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to persist selected game '{_selectedGame.Id}': {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }

    private void ActivateInitialGameSession(GameDescriptor selectedGame)
    {
        var previous = _activeGameSession;
        var previousCts = _gameSessionCts;
        BdoGameSession? candidate = null;

        try
        {
            candidate = _sessionHost.CreateCandidate(selectedGame);
            previousCts?.Cancel();
            DetachSessionHandlers();
            DisposeLocalFileMonitor();
            _localizationNotificationTracker.Reset();
            ClearTransientGameState();

            _sessionHost.CommitCandidate(candidate);
            candidate = null;
            BindGameSession(_sessionHost.CurrentSession, _gameSessionGeneration + 1);
            previousCts?.Dispose();
            previous.Dispose();
        }
        catch
        {
            candidate?.Dispose();
            throw;
        }
    }

    private async Task RunStartupLifecycleMaintenanceAsync()
    {
        try
        {
            await Task.Run(() => _updateLifecycle.RunStartupMaintenance());
        }
        catch (Exception ex)
        {
            _logger.Warning($"Startup lifecycle maintenance failed: {ex.Message}");
        }
    }

    private void TryApplyCachedFeed()
    {
        var cacheLoad = _releaseFeedCacheStore.Load();
        string? validationError = cacheLoad.Error;
        ReleasesResponse? cachedFeed = null;
        if (cacheLoad.Status == FileLoadStatus.Valid && cacheLoad.Value != null
            && !ReleaseFeedCacheMapper.TryToLiveFeed(
                cacheLoad.Value, out cachedFeed, out validationError))
        {
            cachedFeed = null;
        }

        if (cachedFeed == null)
        {
            if (validationError != null)
                _logger.Warning($"Release feed cache unavailable: {validationError}");
            return;
        }

        var snapshot = cacheLoad.Value;
        if (snapshot == null)
            return;

        _apiResponse = cachedFeed;
        _apiLoadedSuccessfully = true;
        _releaseFeedSource = ReleaseFeedSource.Cached;
        _cachedFeedSavedAtUtc = snapshot.SavedAtUtc;
        _apiErrorMessage = null;
        _apiErrorKind = ApiErrorKind.None;
        BuildDynamicModes();
        var config = _configStore.Load().Value ?? new Config();
        RestoreInitialMode(config);
        _logger.Info($"Using cached release feed saved at {_cachedFeedSavedAtUtc:O}.");
    }

    private async Task PersistLiveFeedAsync(ReleasesResponse feed)
    {
        if (!await _releaseFeedCacheStore.SaveAsync(feed))
            _logger.Warning("Live release feed was accepted, but cache persistence failed.");
    }
}
