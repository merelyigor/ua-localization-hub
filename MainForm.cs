using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient;

public partial class MainForm : Form
{
    private ConfigStore _configStore = null!;
    private readonly ApplicationConfigStore _applicationConfigStore;
    private BdoUaApiClient _apiClient = null!;
    private BdoGameSession _activeGameSession = null!;
    private GameDetector _gameDetector = null!;
    private BdoGameDefinition _gameDefinition = null!;
    private readonly GameCatalog _gameCatalog;
    private GameDescriptor _selectedGame;
    private LocalizationStateService _stateService = null!;
    private LocalizationCompatibilityService _compatService = null!;
    private LocalizationInstaller _localizationInstaller = null!;
    private BackupStore _backupStore = null!;
    private InstallationStateStore _stateStore = null!;
    private readonly ILogger _logger;
    private ReleaseFeedPoller _poller = null!;
    private FeedApplicationCoordinator _feedCoordinator = null!;
    private readonly LocalizationNotificationTracker _localizationNotificationTracker = new();
    private readonly ApplicationUpdateNotificationTracker _applicationUpdateNotificationTracker = new();
    private readonly AppVersionInfo _appVersionInfo;
    private readonly GitHubUpdateClient _gitHubClient;
    private readonly UpdateSelectionPolicy _selectionPolicy;
    private readonly AppPaths _appPaths;
    private readonly UpdatePackageService _updatePackageService;
    private readonly UpdateSessionStore _updateSessionStore;
    private readonly SelfUpdatePreparationService _selfUpdatePreparation;
    private readonly UpdateLifecycleService _updateLifecycle;
    private ReleaseFeedCacheStore _releaseFeedCacheStore = null!;
    private readonly SelectedGameSessionHost _sessionHost;
    private readonly HashSet<Task> _sessionWork = new();
    private readonly object _sessionWorkLock = new();
    private CancellationTokenSource? _gameSessionCts;
    private Action<ReleasesResponse>? _sessionFeedCandidateHandler;
    private Action<ReleasesResponse>? _sessionFeedSuccessHandler;
    private long _gameSessionGeneration;
    private bool _suppressGameSelection;
    private volatile bool _switchInProgress;
    private TaskCompletionSource<object?> _switchCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<object?> _startupCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private const string UninstallInstructions =
        "«" + ApplicationBrand.DisplayName + "» — portable-застосунок. Він не встановлюється через Windows Installer і не має окремого деінсталятора у Windows." +
        "\n\n" +
        "Звичайне видалення:\n" +
        "1. Якщо увімкнено автозапуск, вимкніть його в меню трея «Запускати разом із Windows».\n" +
        "2. Повністю завершіть застосунок: відкрийте меню трея та виберіть «Вихід». Натискання X лише ховає застосунок у трей.\n" +
        "3. Видаліть файл " + ApplicationTechnicalIdentity.ExecutableFileName + "." +
        "\n\n" +
        "Повне очищення даних (необов’язково): після завершення застосунку можна видалити папку %LocalAppData%\\" + ApplicationTechnicalIdentity.LocalAppDataDirectoryName + ". У ній можуть зберігатися конфігурація, логи, стан встановлення, тимчасові cache-файли, резервні копії та дані сесій оновлення. Видаляйте цю папку лише якщо хочете втратити ці дані." +
        "\n\n" +
        "Важливо: видалення застосунку або його даних не відновлює і не видаляє локалізацію у Black Desert Online. Якщо потрібно повернути оригінальну локалізацію гри, спочатку використайте в застосунку дію «Відновити оригінал», а вже потім завершіть і видаліть застосунок.";

    private string? _gameRoot;
    private DetectionSource? _gameDetectionSource;
    private GamePatchStatus _gamePatchStatus = GamePatchStatus.Unknown;
    private bool _gamePatchRefreshFailed;
    private ReleasesResponse? _apiResponse;
    private bool _apiLoadedSuccessfully;
    private ReleaseFeedSource _releaseFeedSource = ReleaseFeedSource.Unavailable;
    private DateTimeOffset? _cachedFeedSavedAtUtc;
    private string? _apiErrorMessage;
    private ApiErrorKind _apiErrorKind;
    private bool _initializing;
    private bool _suppressModeChanged;
    private volatile bool _operationInProgress;
    private volatile bool _closing;
    private bool _exitAfterOperation;
    private LocalizationState _lastResolvedState;
    private string? _lastInstalledModeSlug;
    private string? _lastInstalledPublicId;
    private OperationState _operationState = OperationState.Idle;
    private CancellationTokenSource? _operationCts;
    private System.Windows.Forms.Timer? _startupTimer;
    private DateTime _startupStartTime;

    private CancellationTokenSource? _updateCheckCts;
    private Task? _updateCheckTask;
    private System.Windows.Forms.Timer? _applicationUpdateTimer;
    private UpdateCandidate? _pendingUpdateCandidate;
    private UpdateSession? _stagedUpdateSession;
    private volatile bool _updateHandoffInProgress;
    private bool _contentFitScheduled;
    private bool _contentFitInProgress;
    private Size _lastContentFitTargetSize;
    private int _initialClientHeight;

    public MainForm(
        ApplicationConfigStore applicationConfigStore,
        GameCatalog gameCatalog,
        SelectedGameSessionHost sessionHost,
        ILogger logger,
        AppVersionInfo appVersionInfo,
        GitHubUpdateClient gitHubClient,
        UpdateSelectionPolicy selectionPolicy,
        AppPaths appPaths,
        WindowsAutostartService autostartService,
        bool startInBackground,
        SingleInstanceCoordinator singleInstanceCoordinator)
    {
        ArgumentNullException.ThrowIfNull(sessionHost);
        ArgumentNullException.ThrowIfNull(gameCatalog);
        if (!gameCatalog.Games.Any(game =>
                string.Equals(game.Id, sessionHost.CurrentSession.Descriptor.Id, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Game catalog and session must identify a registered game.", nameof(sessionHost));

        _applicationConfigStore = applicationConfigStore;
        _gameCatalog = gameCatalog ?? throw new ArgumentNullException(nameof(gameCatalog));
        _sessionHost = sessionHost;
        _selectedGame = sessionHost.CurrentSession.Descriptor;
        _logger = logger;
        _appVersionInfo = appVersionInfo;
        _gitHubClient = gitHubClient;
        _selectionPolicy = selectionPolicy;
        _appPaths = appPaths;
        _autostartService = autostartService;
        _startInBackground = startInBackground;
        _singleInstanceCoordinator = singleInstanceCoordinator;

        _updateSessionStore = new UpdateSessionStore(appPaths, logger);
        var manifestValidator = new UpdateManifestValidator(logger);
        _updatePackageService = new UpdatePackageService(gitHubClient, manifestValidator, _updateSessionStore, appPaths, logger);
        _selfUpdatePreparation = new SelfUpdatePreparationService(_updateSessionStore, logger);
        _updateLifecycle = new UpdateLifecycleService(_updateSessionStore, appPaths, logger);

        InitializeComponent();
        _initialClientHeight = ClientSize.Height;
        InitializeTray();
        rootScrollPanel.Resize += RootScrollPanel_Resize;
        ApplyTheme();
        InitializeGameSelector();
        BindGameSession(sessionHost.CurrentSession, 1);
        WireEventHandlers();
        this.Shown += MainForm_Shown;
        HandleCreated += (_, _) =>
        {
            WindowChromeHelper.ApplyDarkCaption(this);
            RegisterSecondaryActivationListener();
        };
    }

    private void BindGameSession(BdoGameSession session, long generation)
    {
        ArgumentNullException.ThrowIfNull(session);

        DetachSessionHandlers();
        _activeGameSession = session;
        _gameSessionGeneration = generation;
        _gameSessionCts = new CancellationTokenSource();
        _selectedGame = session.Descriptor;
        gameSectionCaptionLabel.Text = _selectedGame.DisplayName;
        _configStore = session.ConfigStore;
        _apiClient = session.ApiClient;
        _gameDetector = session.GameDetector;
        _gameDefinition = session.GameDefinition;
        _stateService = session.LocalizationStateService;
        _compatService = session.LocalizationCompatibilityService;
        _localizationInstaller = session.LocalizationInstaller;
        _backupStore = session.BackupStore;
        _stateStore = session.InstallationStateStore;
        _releaseFeedCacheStore = session.ReleaseFeedCacheStore;
        _poller = session.ReleaseFeedPoller;
        _feedCoordinator = new FeedApplicationCoordinator(ApplyFeedPipelineAsync, _poller, _logger);
        _sessionFeedCandidateHandler = candidate => QueueSessionFeedWork(session, generation, candidate, isCandidate: true);
        _sessionFeedSuccessHandler = feed => QueueSessionFeedWork(session, generation, feed, isCandidate: false);
        _poller.OnFeedCandidate += _sessionFeedCandidateHandler;
        _poller.OnFeedSuccess += _sessionFeedSuccessHandler;

        _suppressGameSelection = true;
        try
        {
            gameSelectorComboBox.SelectedValue = _selectedGame.Id;
        }
        finally
        {
            _suppressGameSelection = false;
        }
    }

    private void DetachSessionHandlers()
    {
        if (_sessionFeedCandidateHandler != null && _poller != null)
            _poller.OnFeedCandidate -= _sessionFeedCandidateHandler;
        if (_sessionFeedSuccessHandler != null && _poller != null)
            _poller.OnFeedSuccess -= _sessionFeedSuccessHandler;
        _sessionFeedCandidateHandler = null;
        _sessionFeedSuccessHandler = null;
    }

    private bool IsCurrentGameSession(long generation, BdoGameSession? session = null)
        => !_closing
            && generation == _gameSessionGeneration
            && (session == null || ReferenceEquals(session, _activeGameSession))
            && _gameSessionCts is { IsCancellationRequested: false };

    private void TrackSessionWork(Task task)
    {
        lock (_sessionWorkLock)
            _sessionWork.Add(task);

        _ = task.ContinueWith(completed =>
        {
            lock (_sessionWorkLock)
                _sessionWork.Remove(completed);
            if (completed.IsFaulted && completed.Exception != null)
                _logger.Error($"Game session callback failed: {completed.Exception.GetBaseException().Message}");
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task DrainSessionWorkAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_sessionWorkLock)
                pending = _sessionWork.ToArray();
            if (pending.Length == 0)
                return;
            await Task.WhenAll(pending).ConfigureAwait(true);
        }
    }

    private async Task SwitchGameAsync(GameDescriptor target)
    {
        if (_switchInProgress || _operationInProgress || _initializing || _closing)
            return;

        BdoGameSession? candidate = null;
        var committed = false;
        var previous = _activeGameSession;
        var previousCts = _gameSessionCts;
        try
        {
            _switchInProgress = true;
            _switchCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetControlsDuringOperation(false);
            candidate = _sessionHost.CreateCandidate(target);

            previousCts?.Cancel();
            DetachSessionHandlers();
            DisposeLocalFileMonitor();
            _localizationNotificationTracker.Reset();
            ClearTransientGameState();

            await previous.StopAsync();
            await DrainSessionWorkAsync();
            previousCts?.Dispose();

            var replaced = _sessionHost.CommitCandidate(candidate);
            committed = true;
            candidate = null;
            BindGameSession(replaced == previous ? _sessionHost.CurrentSession : replaced, _gameSessionGeneration + 1);
            replaced.Dispose();

            await PersistSelectedGameAsync(target);
            _initializing = true;
            try
            {
                await LoadCurrentGameSessionAsync(_gameSessionGeneration, _gameSessionCts!.Token, runGlobalStartup: false);
            }
            finally
            {
                _initializing = false;
            }

            if (!_closing && IsCurrentGameSession(_gameSessionGeneration, _activeGameSession))
            {
                _poller.Start(_apiResponse);
                _poller.SetPollingMode(Visible ? ReleaseFeedPollingMode.Visible : ReleaseFeedPollingMode.Background);
            }
        }
        catch (OperationCanceledException) when (_closing || !IsCurrentGameSession(_gameSessionGeneration, previous))
        {
            if (!committed)
                _logger.Debug("Game switch was cancelled before the candidate session became active.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Game switch failed: {ex.Message}");
            SetMessage($"Не вдалося перемкнути гру: {ex.Message}");
            if (!committed)
            {
                _suppressGameSelection = true;
                try { gameSelectorComboBox.SelectedValue = previous.Descriptor.Id; }
                finally { _suppressGameSelection = false; }
            }
        }
        finally
        {
            candidate?.Dispose();
            _switchCompletion.TrySetResult(null);
            _switchInProgress = false;
            SetControlsDuringOperation(true);
        }
    }

    private async Task PersistSelectedGameAsync(GameDescriptor descriptor)
    {
        var load = _applicationConfigStore.Load();
        if (load.Status == FileLoadStatus.Invalid)
        {
            _logger.Warning("Application config invalid; selected game was not persisted during switch.");
            return;
        }

        var config = load.Value ?? new ApplicationConfig();
        config.SelectedGameId = descriptor.Id;
        try
        {
            await _applicationConfigStore.SaveAsync(config);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to persist selected game '{descriptor.Id}': {ex.Message}");
        }
    }

    private void ClearTransientGameState()
    {
        _gameRoot = null;
        _gameDetectionSource = null;
        _gamePatchStatus = GamePatchStatus.Unknown;
        _gamePatchRefreshFailed = false;
        _apiResponse = null;
        _apiLoadedSuccessfully = false;
        _releaseFeedSource = ReleaseFeedSource.Unavailable;
        _cachedFeedSavedAtUtc = null;
        _apiErrorMessage = null;
        _apiErrorKind = ApiErrorKind.None;
        _lastResolvedState = LocalizationState.NotInstalled;
        _lastInstalledModeSlug = null;
        _lastInstalledPublicId = null;
        _localFileCheckInProgress = false;
        _localizationNotificationTracker.Reset();
        ClearModeControls();
        SetGameSearching();
        ShowModeLoadingPlaceholder();
        SetProgress(0);
        SetMessage("Завантаження даних для обраної гри...");
    }

    internal GameDescriptor SelectedGame => _selectedGame;
    internal string ActivePersistenceRoot => _activeGameSession.PersistencePaths.Root;
    internal BdoGameSession ActiveGameSession => _activeGameSession;
    internal long GameSessionGeneration => _gameSessionGeneration;
    internal bool IsSwitchInProgress => _switchInProgress;
    internal bool IsOperationInProgress => _operationInProgress;
    internal Task WaitForSwitchCompletionForTestAsync() => _switchCompletion.Task;
    internal Task WaitForStartupCompletionForTestAsync() => _startupCompletion.Task;

    internal Task DeliverSessionFeedForTestAsync(
        BdoGameSession session,
        long generation,
        ReleasesResponse feed)
        => HandleSessionFeedAsync(session, generation, feed, isCandidate: false);

    internal void SetOperationInProgressForTest(bool value)
    {
        _operationInProgress = value;
        SetControlsDuringOperation(!value);
    }
    internal ComboBox GameSelector => gameSelectorComboBox;
    internal string HeaderTitle => headerTitleLabel.Text;
    internal string HeaderSubtitle => headerSubtitleLabel.Text;
    internal string TargetProjectsCaption => targetProjectsCaptionLabel.Text;
    internal string BdoTargetProject => bdoTargetProjectLabel.Text;
    internal string BdoTargetStatus => bdoTargetStatusLabel.Text;
    internal string WwmTargetProject => wwmTargetProjectLabel.Text;
    internal string WwmTargetStatus => wwmTargetStatusLabel.Text;
    internal string ActiveGameSelectorLabel => gameSelectorLabel.Text;
    internal string GameSectionCaption => gameSectionCaptionLabel.Text;
    internal string UninstallHelpText => uninstallHelpLink.Text;
    internal string TrayTooltipText => _notifyIcon.Text;
    internal Size LastContentFitTargetSizeForTest => _lastContentFitTargetSize;
    internal int InitialClientHeightForTest => _initialClientHeight;
    internal int ModeSectionHeightForTest => modeGroupBox.Height;

    private void InitializeGameSelector()
    {
        gameSelectorComboBox.DisplayMember = nameof(GameDescriptor.DisplayName);
        gameSelectorComboBox.ValueMember = nameof(GameDescriptor.Id);
        gameSelectorComboBox.DataSource = _gameCatalog.Games.ToList();
        gameSelectorComboBox.SelectedValue = _selectedGame.Id;
        gameSelectorComboBox.Enabled = _gameCatalog.Games.Count > 1;
    }

    private void WireEventHandlers()
    {
        detectGameButton.Click += DetectGameButton_Click;
        browseGameButton.Click += BrowseGameButton_Click;
        restoreOriginalButton.Click += RestoreOriginalButton_Click;
        cancelButton.Click += CancelButton_Click;
        updateButton.Click += UpdateButton_Click;
        logsButton.Click += LogsButton_Click;
        uninstallHelpLink.LinkClicked += UninstallHelpLink_LinkClicked;
        modesFlowPanel.Resize += ModesFlowPanel_Resize;
        this.FormClosing += MainForm_FormClosing;
        gameSelectorComboBox.SelectedValueChanged += GameSelectorComboBox_SelectedValueChanged;
    }

    private void GameSelectorComboBox_SelectedValueChanged(object? sender, EventArgs e)
    {
        if (_suppressGameSelection)
            return;

        if (_initializing || _switchInProgress || _operationInProgress
            || _closing || _updateHandoffInProgress)
        {
            if (!_closing)
                RestoreGameSelectorToActiveSession();
            return;
        }

        if (gameSelectorComboBox.SelectedValue is not string selectedId)
            return;

        var target = _gameCatalog.Resolve(selectedId);
        if (string.Equals(target.Id, _activeGameSession.Descriptor.Id, StringComparison.OrdinalIgnoreCase))
            return;

        _ = SwitchGameAsync(target);
    }

    private void RestoreGameSelectorToActiveSession()
    {
        _suppressGameSelection = true;
        try
        {
            gameSelectorComboBox.SelectedValue = _activeGameSession.Descriptor.Id;
        }
        finally
        {
            _suppressGameSelection = false;
        }
    }

    private void QueueSessionFeedWork(
        BdoGameSession session,
        long generation,
        ReleasesResponse feed,
        bool isCandidate)
    {
        var task = HandleSessionFeedAsync(session, generation, feed, isCandidate);
        TrackSessionWork(task);
    }

    private async Task HandleSessionFeedAsync(
        BdoGameSession session,
        long generation,
        ReleasesResponse feed,
        bool isCandidate)
    {
        if (!IsCurrentGameSession(generation, session))
            return;

        try
        {
            if (InvokeRequired)
            {
                var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            await HandleSessionFeedOnUiAsync(session, generation, feed, isCandidate);
                            completion.TrySetResult(null);
                        }
                        catch (Exception ex)
                        {
                            completion.TrySetException(ex);
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                await completion.Task.ConfigureAwait(false);
                return;
            }

            await HandleSessionFeedOnUiAsync(session, generation, feed, isCandidate);
        }
        catch (OperationCanceledException) when (!IsCurrentGameSession(generation, session))
        {
        }
        catch (Exception ex)
        {
            _logger.Error($"Feed session callback error: {ex.Message}");
        }
    }

    private async Task HandleSessionFeedOnUiAsync(
        BdoGameSession session,
        long generation,
        ReleasesResponse feed,
        bool isCandidate)
    {
        if (!IsCurrentGameSession(generation, session))
            return;

        if (isCandidate)
        {
            await _feedCoordinator.OnCandidateAsync(feed);
            return;
        }

        if (_releaseFeedSource == ReleaseFeedSource.Cached
            && !FeedChangeDetector.HasSemanticChange(_apiResponse, feed))
        {
            _apiResponse = feed;
            _apiLoadedSuccessfully = true;
            _releaseFeedSource = ReleaseFeedSource.Live;
            _cachedFeedSavedAtUtc = null;
            _apiErrorMessage = null;
            _apiErrorKind = ApiErrorKind.None;
            _poller.AcceptFeed(feed);
            await PersistLiveFeedAsync(feed);
            if (IsCurrentGameSession(generation, session))
            {
                RefreshKnownGamePatchPresentation();
                await RefreshStateAsync(generation, _gameSessionCts!.Token);
            }
        }
    }

    // --- Dynamic modes ---

    // --- Detect button ---

    // --- Browse button ---

    // --- Mode change ---


    // --- FormClosing safety ---

    internal enum MainFormCloseAction
    {
        HideToTray,
        ExitNow,
        DeferUntilOperationCompletes
    }

    /// <summary>
    /// Pure close-policy decision used by MainForm_FormClosing. Self-update
    /// handoff is intentionally excluded and handled as a first branch outside
    /// this helper.
    /// </summary>
    internal static MainFormCloseAction EvaluateCloseAction(
        CloseReason closeReason,
        bool explicitExitRequested,
        bool exitAfterOperation,
        bool operationInProgress)
    {
        // A normal manual X (no explicit tray Exit, no pending synthetic re-close)
        // always hides to tray — even while an operation is active.
        if (closeReason == CloseReason.UserClosing
            && !explicitExitRequested
            && !exitAfterOperation)
        {
            return MainFormCloseAction.HideToTray;
        }

        if (operationInProgress)
            return MainFormCloseAction.DeferUntilOperationCompletes;

        return MainFormCloseAction.ExitNow;
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        // Self-update handoff is a special real-exit path and must remain first.
        // Never convert self-update into hide-to-tray or generic pending-exit.
        if (_updateHandoffInProgress)
        {
            PrepareTrayForShutdown();
            _updateCheckCts?.Cancel();
            _gameSessionCts?.Cancel();
            _poller.Stop();
            return;
        }

        var action = EvaluateCloseAction(
            e.CloseReason, _explicitExitRequested, _exitAfterOperation, _operationInProgress);

        switch (action)
        {
            case MainFormCloseAction.HideToTray:
                // Normal user close (X / Alt+F4) → hide to tray.
                // Must occur before active-operation cancellation logic so an ongoing
                // localization operation is NOT cancelled by simply closing the window.
                e.Cancel = true;
                HideToTray();
                ScheduleAutostartOfferAfterManualHide();
                return;

            case MainFormCloseAction.ExitNow:
                _closing = true;
                _exitAfterOperation = false;
                PrepareTrayForShutdown();
                _updateCheckCts?.Cancel();
                _gameSessionCts?.Cancel();
                _poller.Stop();
                return;

            case MainFormCloseAction.DeferUntilOperationCompletes:
            default:
                // Real close requested while an operation is active: defer termination
                // to protect game-file integrity, request cancellation, and let the
                // operation reach its existing safe cleanup boundary before exiting.
                // This also covers Windows/system close reasons (deferred, not hidden).
                e.Cancel = true;
                _closing = true;
                _exitAfterOperation = true;
                RequestOperationCancelForShutdown();
                return;
        }
    }

    private void RequestOperationCancelForShutdown()
    {
        if (_operationCts != null && !_operationCts.IsCancellationRequested)
        {
            cancelButton.Enabled = false;
            SetMessage("Скасування операції перед закриттям...");
            _operationCts.Cancel();
        }
        else
        {
            SetMessage("Дочекайтеся завершення поточної операції.");
        }
    }

    /// <summary>
    /// Called at the safe completion boundary of an operation. If a real exit was
    /// deferred because the operation was active, schedules the final Close on the
    /// UI thread. The resulting UserClosing re-enters MainForm_FormClosing where
    /// operationInProgress is false and _exitAfterOperation is true → ExitNow.
    /// </summary>
    private void CompletePendingExitAfterOperation()
    {
        if (_exitAfterOperation == false)
            return;
        if (_updateHandoffInProgress)
            return;
        if (IsDisposed || Disposing || !IsHandleCreated)
            return;

        BeginInvoke(new Action(Close));
    }


    // --- Logs button ---

    private void UninstallHelpLink_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        MessageBox.Show(
            this,
            UninstallInstructions,
            $"Як видалити {ApplicationBrand.DisplayName}?",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void LogsButton_Click(object? sender, EventArgs e)
    {
        try
        {
            var logsDir = _appPaths.LogsDir;
            Directory.CreateDirectory(logsDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logsDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open logs folder: {ex.Message}");
            MessageBox.Show(
                "Не вдалося відкрити папку журналів.",
                "Помилка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    // --- Release feed polling ---

    /// <summary>
    /// Whole-pipeline feed application callback used by FeedApplicationCoordinator.
    /// Returns true only if all stages succeed (API update, mode rebuild, selection, state refresh).
    /// </summary>

    // --- Game status presentation ---

    // --- Operation state ---

    // --- State refresh ---


}
