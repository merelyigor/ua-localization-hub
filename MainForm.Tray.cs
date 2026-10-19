using System.Drawing;
using System.Windows.Forms;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient;

public partial class MainForm
{
    private NotifyIcon _notifyIcon = null!;
    private ContextMenuStrip _trayMenu = null!;
    private ToolStripMenuItem _autostartMenuItem = null!;
    private bool _explicitExitRequested;
    private Icon? _trayIcon;
    private readonly IAppNotificationService? _appNotificationService;

    private readonly WindowsAutostartService _autostartService;
    private readonly bool _startInBackground;
    private readonly SingleInstanceCoordinator? _singleInstanceCoordinator;

    private void InitializeTray()
    {
        if (_startInBackground)
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
        }

        _trayMenu = new ContextMenuStrip(components!);

        var openItem = new ToolStripMenuItem("Відкрити");
        openItem.Click += (_, _) => RestoreFromTray();

        var checkNowItem = new ToolStripMenuItem("Перевірити зараз");
        checkNowItem.Click += (_, _) =>
        {
            if (_activeGameSession is BdoGameSession) _poller.RequestImmediatePoll();
            else if (_activeGameSession is WwmGameSession wwm) _ = wwm.Poller.RequestImmediatePollAsync();
            RequestApplicationUpdateCheck();
        };

        _autostartMenuItem = new ToolStripMenuItem("Запускати разом із Windows");
        _autostartMenuItem.Click += (_, _) => ToggleAutostartFromTray();

        var separator = new ToolStripSeparator();

        var exitItem = new ToolStripMenuItem("Вихід");
        exitItem.Click += (_, _) => ExitFromTray();

        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(checkNowItem);
        _trayMenu.Items.Add(_autostartMenuItem);
        _trayMenu.Items.Add(separator);
        _trayMenu.Items.Add(exitItem);

        _trayMenu.Opening += (_, _) => RefreshAutostartMenuItem();

        _notifyIcon = new NotifyIcon(components!)
        {
            ContextMenuStrip = _trayMenu,
            Text = ApplicationBrand.DisplayName,
            Visible = false
        };

        try
        {
            _trayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            _trayIcon = null;
        }

        _notifyIcon.Icon = _trayIcon ?? SystemIcons.Application;

        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();

        // Registered before MainForm_Shown so the form is moved to tray first on
        // background startup, while the normal async startup pipeline still runs after.
        this.Shown += TrayStartup_Shown;
    }

    private void TrayStartup_Shown(object? sender, EventArgs e)
    {
        if (!_startInBackground)
            return;

        _logger.Info("Starting in background mode.");
        HideToTray();
    }

    private void HideToTray()
    {
        _notifyIcon.Visible = true;
        ShowInTaskbar = false;
        Hide();

        if (_activeGameSession is BdoGameSession) _poller.SetPollingMode(ReleaseFeedPollingMode.Background);
        else if (_activeGameSession is WwmGameSession wwm) wwm.Poller.SetVisible(false);

        // T4: only starts the local monitor if a baseline already exists from a prior
        // successful state refresh. It must NOT re-baseline from the current file here.
        StartLocalFileMonitorIfEligible();
    }

    private void ObserveLocalizationNotification(
        string gameId,
        string modeKey,
        string modeName,
        string releaseIdentity,
        string releaseVersion,
        bool updateAvailable)
    {
        var blockedReason = GetTrayNotificationBlockedReason();
        var canNotify = blockedReason == null;
        _logger.Debug($"Tray notification evaluated: type=localization game={gameId} mode={modeKey} identity={releaseIdentity} version={releaseVersion} update_available={updateAvailable.ToString().ToLowerInvariant()} eligible={canNotify.ToString().ToLowerInvariant()} reason={blockedReason ?? "none"}.");

        if (!_localizationNotificationTracker.Observe(
                gameId,
                modeKey,
                releaseIdentity,
                updateAvailable,
                canNotify))
        {
            if (updateAvailable && canNotify)
                _logger.Debug($"Tray notification skipped: type=localization game={gameId} mode={modeKey} identity={releaseIdentity} reason=duplicate_or_pending.");
            return;
        }

        var content = AppNotificationContent.ForLocalizationUpdate(
            gameId, modeKey, modeName, _selectedGame.DisplayName, releaseIdentity, releaseVersion);
        if (LocalizationNotificationForTest is { } notificationObserver)
        {
            notificationObserver(content.Body);
            _localizationNotificationTracker.MarkSubmitted(gameId, modeKey, releaseIdentity);
            return;
        }

        RequestAppNotification(
            content,
            () => _localizationNotificationTracker.MarkSubmitted(gameId, modeKey, releaseIdentity),
            () => _localizationNotificationTracker.MarkFailed(gameId, modeKey, releaseIdentity));
    }

    private void ObserveApplicationUpdateNotification(string? candidateTag)
    {
        var blockedReason = GetTrayNotificationBlockedReason();
        var canNotify = blockedReason == null;
        _logger.Debug($"Tray notification evaluated: type=application_update tag={candidateTag ?? "none"} eligible={canNotify.ToString().ToLowerInvariant()} reason={blockedReason ?? "none"}.");

        if (!_applicationUpdateNotificationTracker.Observe(candidateTag, canNotify))
        {
            if (!string.IsNullOrWhiteSpace(candidateTag) && canNotify)
                _logger.Debug($"Tray notification skipped: type=application_update tag={candidateTag} reason=duplicate_or_pending.");
            return;
        }

        var content = AppNotificationContent.ForApplicationUpdate(candidateTag!);
        RequestAppNotification(
            content,
            () => _applicationUpdateNotificationTracker.MarkSubmitted(candidateTag),
            () => _applicationUpdateNotificationTracker.MarkFailed(candidateTag));
    }

    private string? GetTrayNotificationBlockedReason()
    {
        if (_closing) return "closing";
        if (_updateHandoffInProgress) return "update_handoff";
        if (IsDisposed || Disposing) return "form_disposed";
        if (Visible) return "window_visible";
        if (_notifyIcon == null || !_notifyIcon.Visible) return "tray_icon_hidden";
        return null;
    }

    private void RequestAppNotification(AppNotificationContent content,
        Action markDelivered, Action markFailed)
    {
        if (AppNotificationForTest is { } notificationObserver)
        {
            notificationObserver(content);
            markDelivered();
            return;
        }

        if (_appNotificationService == null || !_appNotificationService.IsRegistered)
        {
            markFailed();
            _logger.Warning($"Windows app notification unavailable: type={content.Type} identity={content.Identity} reason=service_not_registered.");
            return;
        }

        if (_appNotificationService.TryShow(content.Type, content.Identity, content.Title, content.Body))
        {
            markDelivered();
            _logger.Info($"Update notification queued in Windows Action Center: type={content.Type} identity={content.Identity}.");
        }
        else
        {
            markFailed();
            _logger.Warning($"Update notification was not queued in Windows Action Center: type={content.Type} identity={content.Identity}; it remains eligible for retry.");
        }
    }

    private void AppNotificationService_NotificationActivated(string? arguments)
    {
        if (IsDisposed || Disposing || _closing)
            return;

        _logger.Info($"Opening Hub after Windows notification activation: arguments={arguments}.");
        if (InvokeRequired)
        {
            BeginInvoke(new Action(RestoreFromTray));
            return;
        }

        RestoreFromTray();
    }

    private void AppNotificationService_NotificationFailed(string type, string identity)
    {
        void MarkRetryable()
        {
            if (IsDisposed || Disposing)
                return;

            if (string.Equals(type, "application_update", StringComparison.Ordinal))
            {
                _applicationUpdateNotificationTracker.MarkFailed(identity);
                return;
            }

            if (!string.Equals(type, "localization", StringComparison.Ordinal))
                return;

            var firstSeparator = identity.IndexOf('/');
            var lastSeparator = identity.LastIndexOf('/');
            if (firstSeparator <= 0 || lastSeparator <= firstSeparator || lastSeparator == identity.Length - 1)
            {
                _logger.Warning("Failed Windows localization notification has an invalid identity; automatic retry cannot be scheduled.");
                return;
            }

            _localizationNotificationTracker.MarkFailed(
                identity[..firstSeparator],
                identity[(firstSeparator + 1)..lastSeparator],
                identity[(lastSeparator + 1)..]);
        }

        if (IsDisposed || Disposing)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(MarkRetryable));
            return;
        }

        MarkRetryable();
    }

    private void RegisterSecondaryActivationListener()
    {
        if (_singleInstanceCoordinator == null) return;
        if (!_singleInstanceCoordinator.IsPrimary) return;

        // The wait callback runs on a worker thread; marshal to the UI thread and
        // reuse the existing tray restore path rather than duplicating show logic.
        _singleInstanceCoordinator.RegisterActivationCallback(() =>
            BeginInvoke(new Action(TryActivateFromSecondaryInstance)));
    }

    private void TryActivateFromSecondaryInstance()
    {
        if (IsDisposed || Disposing || _closing) return;
        if (_updateHandoffInProgress) return;

        // Restore the existing MainForm without cancelling any active operation.
        RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        if (IsDisposed || Disposing) return;

        ShowInTaskbar = true;
        Show();

        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        EnsureWindowIsVisibleOnScreen();

        Activate();
        BringToFront();

        _notifyIcon.Visible = false;

        // T4: stop the periodic monitor but preserve the committed baseline so a change
        // that occurred while hidden remains comparable after restore.
        StopLocalFileMonitorPreservingBaseline();

        if (_activeGameSession is BdoGameSession)
        {
            _poller.SetPollingMode(ReleaseFeedPollingMode.Visible);
            _poller.RequestImmediatePoll();
        }
        else if (_activeGameSession is WwmGameSession wwm)
        {
            wwm.Poller.SetVisible(true);
            _ = wwm.Poller.RequestImmediatePollAsync();
        }
        RequestApplicationUpdateCheck();

        BeginInvoke(new Action(ReconcileLayoutAfterRestore));
        ScheduleLocalFileCheckAfterRestore();
    }

    private void EnsureWindowIsVisibleOnScreen()
    {
        if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(Bounds)))
            return;

        var workArea = Screen.PrimaryScreen?.WorkingArea
            ?? new Rectangle(0, 0, Math.Max(Width, 800), Math.Max(Height, 600));
        var width = Math.Min(Width, workArea.Width);
        var height = Math.Min(Height, workArea.Height);

        StartPosition = FormStartPosition.Manual;
        Location = new Point(
            workArea.Left + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Top + Math.Max(0, (workArea.Height - height) / 2));
    }

    private void ReconcileLayoutAfterRestore()
    {
        if (IsDisposed || Disposing || _closing) return;

        RefreshModeCardLayout();
        ScheduleContentFit();
    }

    private void ExitFromTray()
    {
        _explicitExitRequested = true;
        Close();
    }

    private void PrepareTrayForShutdown()
    {
        DisposeApplicationUpdateTimer();
        DisposeLocalFileMonitor();

        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;

        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private void RefreshAutostartMenuItem()
    {
        try
        {
            _autostartMenuItem.Checked = _autostartService.IsEnabled();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to read autostart state: {ex.Message}");
        }
    }

    private async void ToggleAutostartFromTray()
    {
        try
        {
            bool current = _autostartService.IsEnabled();
            if (current)
                _autostartService.Disable();
            else
                _autostartService.Enable();

            bool actual = _autostartService.IsEnabled();
            _autostartMenuItem.Checked = actual;
            _logger.Info(actual ? "Autostart enabled from tray." : "Autostart disabled from tray.");

            await MarkAutostartPromptDismissedAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to change autostart setting: {ex.Message}");
            RefreshAutostartMenuItem();
            MessageBox.Show(
                "Не вдалося змінити параметр автозапуску.",
                "Автозапуск",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ScheduleAutostartOfferAfterManualHide()
    {
        if (_operationInProgress)
            return;

        BeginInvoke(new Action(() =>
        {
            if (IsDisposed || Disposing || _closing)
                return;
            if (_operationInProgress)
                return;
            if (Visible)
                return;

            OfferAutostartIfEligible();
        }));
    }

    private async void OfferAutostartIfEligible()
    {
        try
        {
            if (_autostartService.IsEnabled())
                return;

            var load = _applicationConfigStore.Load();
            if (load.Status == FileLoadStatus.Invalid)
            {
                _logger.Warning("Config invalid; skipping autostart prompt.");
                return;
            }

            if (load.Value != null && load.Value.AutostartPromptDismissed)
                return;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Autostart prompt eligibility check failed: {ex.Message}");
            return;
        }

        var result = MessageBox.Show(
            $"Запускати «{ApplicationBrand.DisplayName}» разом із Windows?\nЗастосунок автоматично запускатиметься у фоновому режимі в області сповіщень.",
            "Автозапуск",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
            await AcceptAutostartPromptAsync();
        else
            await DismissAutostartPromptAsync();
    }

    private async Task AcceptAutostartPromptAsync()
    {
        try
        {
            _autostartService.Enable();

            if (_autostartService.IsEnabled())
            {
                _logger.Info("Autostart enabled via prompt.");
                RefreshAutostartMenuItem();
                await MarkAutostartPromptDismissedAsync();
            }
            else
            {
                _logger.Warning("Autostart enable did not take effect.");
                ShowAutostartWarning("Не вдалося увімкнути автозапуск.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to enable autostart: {ex.Message}");
            ShowAutostartWarning("Не вдалося увімкнути автозапуск.");
        }
    }

    private async Task DismissAutostartPromptAsync()
    {
        try
        {
            await MarkAutostartPromptDismissedAsync();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to save autostart dismissal: {ex.Message}");
            ShowAutostartWarning("Не вдалося зберегти вибір автозапуску.");
        }
    }

    private async Task MarkAutostartPromptDismissedAsync()
    {
        var load = _applicationConfigStore.Load();
        if (load.Status == FileLoadStatus.Invalid)
        {
            _logger.Warning("Config invalid; not saving autostart prompt dismissal.");
            return;
        }

        var config = load.Value ?? new ApplicationConfig { SelectedGameId = _selectedGame.Id };
        if (config.AutostartPromptDismissed)
            return;

        config.AutostartPromptDismissed = true;
        await _applicationConfigStore.SaveAsync(config);
    }

    private void ShowAutostartWarning(string message)
    {
        MessageBox.Show(message, "Автозапуск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
