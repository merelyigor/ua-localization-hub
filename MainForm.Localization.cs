using System.Drawing;
using System.Windows.Forms;
using BdoClient.Api;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient;

public partial class MainForm
{

    private void BuildDynamicModes()
    {
        if (_wwmSession != null)
        {
            BuildWwmModes();
            return;
        }
        var allModes = _apiResponse?.Data?.Modes;
        var installable = DynamicModePolicy.GetInstallableModes(allModes);

        ClearModeControls();

        if (installable.Count == 0)
        {
            var localPatch = _gameDefinition.TryReadInstalledPatch(_gameRoot);
            var officialPatch = _apiResponse?.Data?.OfficialPatch;
            var patch = localPatch ?? (officialPatch > 0 ? officialPatch : null);
            var label = new Label
            {
                Text = patch.HasValue
                    ? $"Для патча {patch.Value} поки немає доступних режимів локалізації."
                    : "Наразі немає доступних режимів.",
                AutoSize = true,
                ForeColor = UiTheme.SecondaryText,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            AddModePlaceholder(label);
            EnsureMinimumUsableWidth();
            ScheduleContentFit();
            return;
        }

        foreach (var mode in installable)
        {
            var card = new LocalizationModeCard(mode);
            card.SelectionRequested += ModeCard_SelectionRequested;
            card.ActionRequested += ModeCard_ActionRequested;
            modesFlowPanel.Controls.Add(card);
        }
        EnsureMinimumUsableWidth();
        RefreshModeCardLayout();
        ScheduleContentFit();
    }

    private void BuildWwmModes()
    {
        ClearModeControls();
        var modes = WwmPackageResolver.GetInstallableModes(_wwmFeed);
        if (modes.Count == 0)
        {
            AddModePlaceholder(new Label { Text = _wwmFeed?.Success == true
                    ? "Наразі немає доступних режимів локалізації."
                    : "Не вдалося завантажити режими Winds4UA.", AutoSize = true,
                ForeColor = UiTheme.SecondaryText, BackColor = Color.Transparent, Margin = new Padding(0) });
        }
        else
        {
            foreach (var mode in modes)
            {
                var card = new WwmModeCard(mode);
                card.SelectionRequested += ModeCard_SelectionRequested;
                card.ActionRequested += ModeCard_ActionRequested;
                modesFlowPanel.Controls.Add(card);
            }
            var savedMode = _wwmSession?.ConfigStore.Load().Value;
            var selected = modes.FirstOrDefault(mode => mode.Slug == savedMode?.LastMode
                && mode.Variant == savedMode?.LastModeVariant)
                ?? modes.FirstOrDefault(mode => mode.Slug == savedMode?.LastMode)
                ?? modes[0];
            SelectWwmMode(selected.Slug!, selected.Variant!);
            RefreshModeCardLayout();
        }
        EnsureMinimumUsableWidth();
        ScheduleContentFit();
    }

    private async Task RefreshWwmStateAsync(long? expectedGeneration = null, CancellationToken cancellationToken = default)
    {
        var session = _wwmSession;
        if (session == null) return;
        var generation = expectedGeneration ?? _gameSessionGeneration;
        if (expectedGeneration.HasValue && !IsCurrentGameSession(generation, session)) return;
        if (_wwmRecoveryBlocked)
        {
            ApplyWwmRecoveryBlockedPresentation();
            return;
        }
        if (_gameRoot == null)
        {
            DisposeWwmFileMonitor();
            SetActionsEnabled(false);
            SetMessage("Гру не знайдено. Натисніть «Знайти автоматично» або оберіть папку.");
            foreach (var card in modesFlowPanel.Controls.OfType<WwmModeCard>())
                card.Present(WwmModeCardPresentation.Create(false, WwmInstalledStateKind.Unknown,
                    stateTrusted: true, recoveryBlocked: _wwmRecoveryBlocked, gameDetected: false, operationInProgress: _operationInProgress));
            ScheduleContentFit();
            return;
        }

        var installed = session.StateStore.Load(out var stateError);
        if (stateError != null)
        {
            SetMessage("Збережений стан WWM пошкоджено. Встановлення та відновлення заблоковано.");
            SetActionsEnabled(false);
        }
        restoreOriginalButton.Text = "Відновити оригінал";
        var restoreAvailability = stateError != null
            ? (IsAvailable: false, Message: (string?)null)
            : await session.InstallService.CheckRestoreAvailabilityAsync(_gameRoot, session.SteamBuildId, cancellationToken).ConfigureAwait(true);
        if (expectedGeneration.HasValue && !IsCurrentGameSession(generation, session)) return;
        SetActionsEnabled(restoreAvailability.IsAvailable && !_operationInProgress);
        if (!restoreAvailability.IsAvailable && !string.IsNullOrWhiteSpace(restoreAvailability.Message))
            SetMessage(restoreAvailability.Message);
        foreach (var card in modesFlowPanel.Controls.OfType<WwmModeCard>())
        {
            var hasPackage = WwmPackageResolver.TryResolve(_wwmFeed, card.ModeSlug, card.ModeVariant, out var package, out _);
            var state = installed == null
                ? new WwmInstalledStateResult(WwmInstalledStateKind.Unknown)
                : await session.InstallService.ResolveStateAsync(_gameRoot, hasPackage ? package : null, cancellationToken).ConfigureAwait(true);
            if (expectedGeneration.HasValue && !IsCurrentGameSession(generation, session)) return;
            var sameMode = installed?.ModeSlug == card.ModeSlug && installed.ModeVariant == card.ModeVariant;
            var presentation = WwmModeCardPresentation.Create(sameMode, state.State,
                stateTrusted: stateError == null, recoveryBlocked: _wwmRecoveryBlocked, gameDetected: true, operationInProgress: _operationInProgress);
            card.Present(presentation);
        }
        var overallState = installed == null
            ? new WwmInstalledStateResult(WwmInstalledStateKind.Unknown)
            : await session.InstallService.ResolveStateAsync(_gameRoot, null, cancellationToken).ConfigureAwait(true);
        if (stateError == null && installed != null && overallState.State == WwmInstalledStateKind.Modified)
        {
            SetMessage("Керовані файли WWM змінені поза Хабом. Автоматичні операції заблоковано.");
            SetActionsEnabled(false);
        }
        if (installed != null && stateError == null) StartWwmFileMonitorIfEligible();
        else DisposeWwmFileMonitor();
        ScheduleContentFit();
    }

    private void ApplyWwmRecoveryBlockedPresentation()
    {
        DisposeWwmFileMonitor();
        restoreOriginalButton.Text = "Відновити оригінал";
        SetActionsEnabled(false);
        foreach (var card in modesFlowPanel.Controls.OfType<WwmModeCard>())
            card.Present(WwmModeCardPresentation.Create(false, WwmInstalledStateKind.Unknown,
                stateTrusted: false, recoveryBlocked: true, gameDetected: false, operationInProgress: _operationInProgress));
        SetMessage(_wwmRecoveryMessage ?? "Не вдалося безпечно відновити незавершену операцію WWM.");
        ScheduleContentFit();
    }

    private void EnterWwmRecoveryBlockedState(string? message)
    {
        _wwmRecoveryBlocked = true;
        _wwmRecoveryMessage = string.IsNullOrWhiteSpace(message)
            ? "Критична помилка відновлення WWM. Подальші зміни заблоковано."
            : message;
        ApplyWwmRecoveryBlockedPresentation();
    }

    private static bool RequiresWwmRecoveryBlock(WwmMutationResult result) =>
        !result.IsSuccess && result.Error is WwmMutationError.RollbackFailed
            or WwmMutationError.RecoveryRequired
            or WwmMutationError.SnapshotStale;

    private void SetWwmGamePresentation(WwmDetectionResult result)
    {
        _gameRoot = result.GameRoot;
        if (result.GameRoot == null)
        {
            _gameDetectionSource = null;
            gameStatusLabel.Text = "Steam-гру не знайдено";
            gameStatusLabel.ForeColor = UiTheme.SecondaryText;
            gamePathLabel.Text = "";
        }
        else
        {
            _gameDetectionSource = DetectionSource.Steam;
            gameStatusLabel.Text = string.IsNullOrWhiteSpace(result.BuildId)
                ? "Знайдено Steam-гру · build невідомий"
                : $"Знайдено Steam-гру · build {result.BuildId} (сумісність не підтверджено)";
            gameStatusLabel.ForeColor = UiTheme.Success;
            gamePathLabel.Text = result.GameRoot;
        }
        detectGameButton.Text = result.GameRoot == null ? "Знайти автоматично" : "Перевірити";
    }

    private async Task HandleWwmInstallAsync(string slug, string variant)
    {
        var session = _wwmSession;
        if (session == null || _operationInProgress || _gameRoot == null || _wwmRecoveryBlocked) return;
        _operationInProgress = true;
        _operationCts = CancellationTokenSource.CreateLinkedTokenSource(_gameSessionCts?.Token ?? CancellationToken.None);
        var token = _operationCts.Token;
        session.Poller.Pause();
        SetControlsDuringOperation(false);
        SetOperationState(OperationState.LoadingApi);
        try
        {
            var latest = await session.ApiClient.GetLatestAsync(token);
            if (!latest.IsSuccess || latest.Feed == null)
            {
                SetOperationState(latest.IsCancelled ? OperationState.Cancelled : OperationState.Failed);
                SetMessage("Не вдалося перевірити актуальний реліз Winds4UA. Встановлення не виконано.");
                return;
            }
            if (!WwmPackageResolver.TryResolve(latest.Feed, slug, variant, out var package, out var packageError))
            {
                SetOperationState(OperationState.Failed);
                _logger.Warning($"WWM install blocked by fresh API metadata: mode={slug}/{variant}, detail={packageError}");
                SetMessage("Вибраний режим Winds4UA зараз недоступний або його метадані некоректні.");
                _wwmFeed = latest.Feed;
                BuildWwmModes();
                return;
            }
            if (!(WwmCompatibilityConfirmationForTest?.Invoke() ?? GameTestConfirmationDialog.ShowNeutralConfirmation(this,
                    "Сумісність із поточним Steam build не підтверджена",
                    "API не підтверджує сумісність цього перекладу з конкретною версією гри. Ви можете продовжити або повернутися назад.")))
            {
                SetOperationState(OperationState.Cancelled);
                SetMessage("Встановлення скасовано.");
                return;
            }
            token.ThrowIfCancellationRequested();
            if (!IsCurrentGameSession(_gameSessionGeneration, session)) return;
            SetOperationState(OperationState.Downloading);
            cancelButton.Visible = true;
            cancelButton.Enabled = true;
            var result = WwmInstallResultForTest != null
                ? await WwmInstallResultForTest(token)
                : await session.InstallService.InstallAsync(_gameRoot, session.SteamBuildId, package!, token);
            SetOperationState(result.IsSuccess ? OperationState.Completed : OperationState.Failed);
            if (RequiresWwmRecoveryBlock(result))
            {
                _logger.Error($"WWM install left unresolved recovery evidence: {result.Error}: {result.Message}");
                EnterWwmRecoveryBlockedState(result.Message);
            }
            else
            {
                SetMessage(result.IsSuccess ? "Локалізацію Winds4UA успішно встановлено." : result.Message ?? "Операцію Winds4UA не виконано.");
            }
            _wwmFeed = latest.Feed;
        }
        catch (OperationCanceledException)
        {
            SetOperationState(OperationState.Cancelled);
            SetMessage("Операцію скасовано.");
        }
        catch (Exception ex)
        {
            _logger.Error($"WWM install failed: {ex.Message}");
            SetOperationState(OperationState.Failed);
            SetMessage("Не вдалося виконати операцію Winds4UA.");
        }
        finally
        {
            cancelButton.Visible = false;
            cancelButton.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = null;
            _operationInProgress = false;
            SetControlsDuringOperation(true);
            if (IsCurrentGameSession(_gameSessionGeneration, session))
                await RefreshWwmStateAsync(_gameSessionGeneration, _gameSessionCts?.Token ?? default);
            session.Poller.Resume();
        }
    }

    private async Task<WwmMutationResult> HandleWwmRestoreAsync()
    {
        var session = _wwmSession;
        if (session == null || _operationInProgress || _gameRoot == null || _wwmRecoveryBlocked)
            return WwmMutationResult.Failure(WwmMutationError.InvalidRoot, "Гру не знайдено.");
        _operationInProgress = true;
        _operationCts = CancellationTokenSource.CreateLinkedTokenSource(_gameSessionCts?.Token ?? CancellationToken.None);
        var token = _operationCts.Token;
        session.Poller.Pause();
        SetControlsDuringOperation(false);
        SetOperationState(OperationState.Restoring);
        cancelButton.Visible = true;
        cancelButton.Enabled = true;
        try
        {
            var result = await session.InstallService.RestorePreHubAsync(_gameRoot, session.SteamBuildId, token);
            SetOperationState(result.IsSuccess ? OperationState.Completed : OperationState.Failed);
            if (RequiresWwmRecoveryBlock(result))
            {
                _logger.Error($"WWM restore left unresolved recovery evidence: {result.Error}: {result.Message}");
                EnterWwmRecoveryBlockedState(result.Message);
            }
            else
            {
                SetMessage(result.IsSuccess ? "Попередній стан файлів відновлено." : result.Message ?? "Відновлення не виконано.");
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            SetOperationState(OperationState.Cancelled);
            SetMessage("Відновлення скасовано.");
            return WwmMutationResult.Failure(WwmMutationError.Cancelled, "Відновлення скасовано.");
        }
        catch (Exception ex)
        {
            _logger.Error($"WWM restore failed: {ex.Message}");
            SetOperationState(OperationState.Failed);
            const string recoveryMessage = "Не вдалося безпечно відновити попередній стан WWM.";
            EnterWwmRecoveryBlockedState(recoveryMessage);
            return WwmMutationResult.Failure(WwmMutationError.RecoveryRequired, recoveryMessage);
        }
        finally
        {
            cancelButton.Visible = false;
            cancelButton.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = null;
            _operationInProgress = false;
            SetControlsDuringOperation(true);
            await RefreshWwmStateAsync(_gameSessionGeneration, _gameSessionCts?.Token ?? default);
            session.Poller.Resume();
        }
    }


    private void RefreshModeCardLayout()
    {
        if (modesFlowPanel == null) return;
        var availableWidth = modeGroupBox.ClientSize.Width - modesFlowPanel.Margin.Horizontal;
        if (availableWidth <= 0)
            availableWidth = ClientSize.Width - mainLayoutPanel.Padding.Horizontal;
        modesFlowPanel.Width = Math.Max(UiTheme.Scale(modesFlowPanel, 240), availableWidth);
        var width = Math.Max(UiTheme.Scale(modesFlowPanel, 240), modesFlowPanel.ClientSize.Width - modesFlowPanel.Padding.Horizontal);
        var minimumCardWidth = UiTheme.Scale(modesFlowPanel, MinimumModeCardWidth);
        var gap = UiTheme.Scale(modesFlowPanel, ModeCardGap);
        var threeColumnWidth = minimumCardWidth * 3 + gap * 2;
        var twoColumnWidth = minimumCardWidth * 2 + gap;
        var columns = width >= threeColumnWidth ? 3
            : width >= twoColumnWidth ? 2 : 1;
        var cardWidth = Math.Max(minimumCardWidth, (width - gap * (columns - 1)) / columns);
        var cardHeight = UiTheme.Scale(modesFlowPanel, 220);
        var cards = modesFlowPanel.Controls.Cast<Control>()
            .Where(control => control is LocalizationModeCard or WwmModeCard).ToList();
        foreach (var card in cards)
        {
            card.Width = cardWidth;
            card.Height = Math.Max(cardHeight, card.GetPreferredSize(new Size(cardWidth, 0)).Height);
            cardHeight = Math.Max(cardHeight, card.Height);
        }
        var cardCount = cards.Count;
        var rows = Math.Max(1, (int)Math.Ceiling(cardCount / (double)columns));
        for (var index = 0; index < cards.Count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            cards[index].Bounds = new Rectangle(
                modesFlowPanel.Padding.Left + column * (cardWidth + gap),
                modesFlowPanel.Padding.Top + row * (cardHeight + gap),
                cardWidth,
                cardHeight);
        }
        if (cardCount == 0)
        {
            var placeholder = modesFlowPanel.Controls.Cast<Control>().FirstOrDefault();
            if (placeholder != null)
            {
                RefreshModePlaceholderLayout(placeholder);
            }
            else
            {
                modesFlowPanel.Height = modesFlowPanel.Padding.Top + UiTheme.Scale(modesFlowPanel, 56);
                UpdateModeSectionHeight();
            }
        }
        else
        {
            modesFlowPanel.Height = modesFlowPanel.Padding.Top + rows * cardHeight + (rows - 1) * gap;
            UpdateModeSectionHeight();
        }
        modesFlowPanel.PerformLayout();
    }


    private void AddModePlaceholder(Label label)
    {
        modesFlowPanel.Controls.Add(label);
        RefreshModePlaceholderLayout(label);
    }


    private void RefreshModePlaceholderLayout(Control placeholder)
    {
        var availableWidth = Math.Max(1,
            modesFlowPanel.ClientSize.Width
            - modesFlowPanel.Padding.Horizontal
            - placeholder.Margin.Horizontal);
        placeholder.MaximumSize = new Size(availableWidth, 0);
        var preferredSize = placeholder.GetPreferredSize(new Size(availableWidth, 0));
        placeholder.Size = preferredSize;
        placeholder.Location = new Point(
            modesFlowPanel.Padding.Left + placeholder.Margin.Left,
            modesFlowPanel.Padding.Top + placeholder.Margin.Top);
        modesFlowPanel.Height = ModeSectionLayoutPolicy.CalculateContentPanelHeight(
            modesFlowPanel.Padding,
            preferredSize.Height,
            placeholder.Margin);
        UpdateModeSectionHeight();
    }


    private void UpdateModeSectionHeight()
    {
        modeGroupBox.Height = ModeSectionLayoutPolicy.CalculateSectionHeight(
            modeSectionCaptionLabel.PreferredHeight,
            modeSectionCaptionLabel.Margin,
            modesFlowPanel.Height);
    }


    private void RestoreInitialMode(Config config)
    {
        var allModes = _apiResponse?.Data?.Modes;
        var installable = DynamicModePolicy.GetInstallableModes(allModes);
        var installedModeSlug = GetInstalledModeSlugForInitialSelection();
        var selectedSlug = DynamicModePolicy.ResolveInitialSelection(
            installedModeSlug, config.LastMode, installable);

        if (selectedSlug != null)
            SelectModeBySlug(selectedSlug);
    }


    private string? GetInstalledModeSlugForInitialSelection()
    {
        var installedLoad = _stateStore.Load();
        if (installedLoad.Status != FileLoadStatus.Valid
            || installedLoad.Value?.Source != InstallationSource.Api
            || string.IsNullOrWhiteSpace(installedLoad.Value.ModeSlug))
        {
            return null;
        }

        return installedLoad.Value.ModeSlug;
    }


    private void ShowModeLoadingPlaceholder()
    {
        ClearModeControls();
        var label = new Label
        {
            Text = "Завантаження доступних режимів...",
            AutoSize = true,
            ForeColor = UiTheme.SecondaryText,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        AddModePlaceholder(label);
        EnsureMinimumUsableWidth();
        ScheduleContentFit();
    }


    private void ShowModeFailurePlaceholder()
    {
        ClearModeControls();
        var label = new Label
        {
            Text = "Не вдалося завантажити режими.",
            AutoSize = true,
            ForeColor = UiTheme.SecondaryText,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        AddModePlaceholder(label);
        EnsureMinimumUsableWidth();
        ScheduleContentFit();
    }


    private void ClearModeControls()
    {
        foreach (var card in modesFlowPanel.Controls.OfType<LocalizationModeCard>().ToList())
            card.Dispose();
        foreach (var card in modesFlowPanel.Controls.OfType<WwmModeCard>().ToList())
            card.Dispose();
        modesFlowPanel.Controls.Clear();
    }


    private void SelectModeBySlug(string slug)
    {
        Control? selected = modesFlowPanel.Controls
            .OfType<LocalizationModeCard>()
            .FirstOrDefault(card => string.Equals(card.ModeSlug, slug, StringComparison.Ordinal));
        selected ??= modesFlowPanel.Controls.OfType<WwmModeCard>()
            .FirstOrDefault(card => string.Equals(card.ModeSlug, slug, StringComparison.Ordinal));
        selected ??= modesFlowPanel.Controls.OfType<LocalizationModeCard>().FirstOrDefault();
        selected ??= modesFlowPanel.Controls.OfType<WwmModeCard>().FirstOrDefault();
        foreach (var card in modesFlowPanel.Controls.OfType<LocalizationModeCard>())
            card.IsSelected = ReferenceEquals(card, selected);
        foreach (var card in modesFlowPanel.Controls.OfType<WwmModeCard>())
            card.IsSelected = ReferenceEquals(card, selected);
    }

    private void SelectWwmMode(string slug, string variant)
    {
        WwmModeCard? selected = modesFlowPanel.Controls.OfType<WwmModeCard>()
            .FirstOrDefault(card => string.Equals(card.ModeSlug, slug, StringComparison.Ordinal)
                && string.Equals(card.ModeVariant, variant, StringComparison.Ordinal));
        selected ??= modesFlowPanel.Controls.OfType<WwmModeCard>().FirstOrDefault();
        foreach (var card in modesFlowPanel.Controls.OfType<WwmModeCard>())
            card.IsSelected = ReferenceEquals(card, selected);
    }

    private async Task PersistWwmModeSelectionAsync(WwmModeCard card)
    {
        var session = _wwmSession;
        if (session == null) return;
        try
        {
            var config = session.ConfigStore.Load().Value ?? new Config();
            config.LastMode = card.ModeSlug;
            config.LastModeVariant = card.ModeVariant;
            await session.ConfigStore.SaveAsync(config, _gameSessionCts?.Token ?? default);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _logger.Warning($"Failed to save WWM mode selection: {ex.Message}");
        }
    }


    private string? GetSelectedModeSlug()
    {
        string? found = null;
        int count = 0;

        foreach (var card in modesFlowPanel.Controls.OfType<LocalizationModeCard>())
        {
            if (card.IsSelected)
            {
                found = card.ModeSlug;
                count++;
            }
        }
        foreach (var card in modesFlowPanel.Controls.OfType<WwmModeCard>())
        {
            if (!card.IsSelected) continue;
            found = card.ModeSlug;
            count++;
        }

        if (count > 1)
        {
            _logger.Error($"Ambiguous mode selection: {count} mode cards selected");
            return null;
        }

        return found;
    }


    private LocalizationMode? GetSelectedApiMode()
    {
        var slug = GetSelectedModeSlug();
        if (slug == null || _apiResponse?.Data?.Modes == null) return null;
        return _apiResponse.Data.Modes
            .FirstOrDefault(m => string.Equals(m.Slug, slug, StringComparison.Ordinal));
    }


    private async void DetectGameButton_Click(object? sender, EventArgs e)
    {
        if (_operationInProgress || _switchInProgress || _initializing || _closing)
            return;

        var generation = _gameSessionGeneration;
        var session = _activeGameSession;
        var cancellationToken = _gameSessionCts?.Token ?? default;
        if (_wwmSession is { } wwm)
        {
            _operationInProgress = true;
            SetControlsDuringOperation(false);
            SetOperationState(OperationState.DetectingGame);
            try
            {
                var found = wwm.DetectGame();
                _gameRoot = found.GameRoot;
                SetWwmGamePresentation(found);
                await RefreshStateAsync(_gameSessionGeneration, cancellationToken);
            }
            finally
            {
                _operationInProgress = false;
                SetOperationState(OperationState.Idle);
                SetControlsDuringOperation(true);
            }
            return;
        }
        _operationInProgress = true;
        detectGameButton.Enabled = false;
        var previousGameRoot = _gameRoot;
        SetOperationState(OperationState.DetectingGame);
        SetGameSearching();
        try
        {
            var patterns = _apiResponse?.Data?.InstallPathPatterns;
            var result = await _gameDetector.DetectAsync(patterns, cancellationToken);
            if (!IsCurrentGameSession(generation, session))
                return;
            if (result.IsFound && result.GamePath != null)
            {
                _gameRoot = result.GamePath;
                SetGameFound(result.GamePath, result.Source);
                await RefreshStateAsync();
            }
            else
            {
                _gameRoot = null;
                SetGameNotFound("Гру не знайдено");
                await RefreshStateAsync();
            }
        }
        catch (Exception ex)
        {
            if (!IsCurrentGameSession(generation, session))
                return;
            _logger.Error($"Detection error: {ex.Message}");
            if (previousGameRoot != null && _gameDetector.IsValidGamePath(previousGameRoot))
            {
                _gameRoot = previousGameRoot;
                SetGameFound(previousGameRoot, null);
                SetMessage($"Помилка пошуку: {ex.Message}");
            }
            else
            {
                _gameRoot = null;
                SetGameNotFound("Помилка пошуку гри");
                SetMessage($"Помилка пошуку: {ex.Message}");
            }
        }
        finally
        {
            _operationInProgress = false;
            detectGameButton.Enabled = true;
            SetOperationState(OperationState.Idle);
        }
    }


    private async void BrowseGameButton_Click(object? sender, EventArgs e)
    {
        if (_wwmSession is { } wwm)
        {
            using var wwmDialog = new FolderBrowserDialog { Description = "Оберіть папку Where Winds Meet або її батьківську папку" };
            if (wwmDialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                if (!await wwm.ValidateAndSaveManualPathAsync(wwmDialog.SelectedPath, _gameSessionCts?.Token ?? default))
                {
                    SetMessage("У вибраній папці не знайдено потрібні файли Where Winds Meet.");
                    return;
                }
                _gameRoot = wwm.GameRoot;
                SetWwmGamePresentation(new WwmDetectionResult(wwm.GameRoot, wwm.SteamBuildId, null));
                await RefreshStateAsync(_gameSessionGeneration, _gameSessionCts?.Token ?? default);
            }
            catch (Exception ex) { _logger.Warning($"WWM manual path selection failed: {ex.Message}"); SetMessage("Не вдалося перевірити шлях Where Winds Meet."); }
            return;
        }
        if (_operationInProgress || _switchInProgress || _initializing || _closing)
            return;

        var generation = _gameSessionGeneration;
        var session = _activeGameSession;
        _operationInProgress = true;
        try
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = $"Оберіть папку гри {_gameDefinition.DisplayName} або її батьківську папку"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var resolved = _gameDetector.ResolveManualGameRootForSelection(dialog.SelectedPath);

            if (resolved.Status == ManualResolveStatus.Found && resolved.GamePath != null)
            {
                var result = await _gameDetector.ValidateAndSaveManualPathAsync(resolved.GamePath);
                if (!IsCurrentGameSession(generation, session))
                    return;
                if (result.IsFound && result.GamePath != null)
                {
                    _gameRoot = result.GamePath;
                    SetGameFound(result.GamePath, result.Source);
                    SetMessage("Папку гри успішно визначено.");
                    await RefreshStateAsync();
                }
                else
                {
                    SetManualFailureMessage("У вибраній папці гру не знайдено.");
                }
            }
            else if (resolved.Status == ManualResolveStatus.Ambiguous)
            {
                SetManualFailureMessage("Знайдено кілька папок з грою. Оберіть точну папку гри.");
            }
            else
            {
                SetManualFailureMessage("У вибраній папці гру не знайдено.");
            }
        }
        catch (Exception ex)
        {
            if (!IsCurrentGameSession(generation, session))
                return;
            _logger.Error($"Browse error: {ex.Message}");
            SetMessage($"Помилка вибору папки: {ex.Message}");
        }
        finally
        {
            _operationInProgress = false;
        }
    }


    private void SetManualFailureMessage(string message)
    {
        if (_gameRoot != null && _gameDetector.IsValidGamePath(_gameRoot))
        {
            // Keep existing valid game status, show transient error only
            SetMessage(message);
        }
        else
        {
            SetGameNotFound(message);
        }
    }


    private async void ModeCard_SelectionRequested(object? sender, EventArgs e)
    {
        if (_initializing) return;
        if (_suppressModeChanged) return;
        if (_wwmSession != null && sender is WwmModeCard selectedWwmCard)
        {
            if (_operationInProgress) return;
            SelectWwmMode(selectedWwmCard.ModeSlug, selectedWwmCard.ModeVariant);
            await PersistWwmModeSelectionAsync(selectedWwmCard);
            await RefreshWwmStateAsync(_gameSessionGeneration, _gameSessionCts?.Token ?? default);
            return;
        }
        var slug = sender switch
        {
            LocalizationModeCard bdoCard when bdoCard.Enabled => bdoCard.ModeSlug,
            WwmModeCard wwmCard => wwmCard.ModeSlug,
            _ => null
        };
        if (slug == null) return;

        try
        {
            var previousSlug = GetSelectedModeSlug();
            SelectModeBySlug(slug);
            if (string.Equals(previousSlug, slug, StringComparison.Ordinal)) return;
            string? configWarning = null;

            try
            {
                var configLoad = _configStore.Load();
                var config = configLoad.Value ?? new Config();
                config.LastMode = slug;
                await _configStore.SaveAsync(config);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save mode config: {ex.Message}");
                configWarning = "Не вдалося зберегти налаштування режиму.";
            }

            SetProgress(0);
            await RefreshStateAsync();

            if (configWarning != null)
            {
                var existingMessage = operationMessageLabel.Text;
                SetMessage(string.IsNullOrWhiteSpace(existingMessage)
                    ? configWarning
                    : $"{configWarning}{Environment.NewLine}{Environment.NewLine}{existingMessage}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Mode change error: {ex.Message}");
            SetMessage($"Помилка зміни режиму: {ex.Message}");
        }
    }


    private async void ModeCard_ActionRequested(object? sender, EventArgs e)
    {
        if (_wwmSession != null && sender is WwmModeCard wwmCard)
        {
            if (_initializing || _suppressModeChanged || _operationInProgress || !wwmCard.Mode.Available) return;
            SelectWwmMode(wwmCard.ModeSlug, wwmCard.ModeVariant);
            await PersistWwmModeSelectionAsync(wwmCard);
            await HandleWwmInstallAsync(wwmCard.ModeSlug, wwmCard.ModeVariant);
            return;
        }
        if (_initializing || _suppressModeChanged || _operationInProgress || sender is not LocalizationModeCard card)
            return;

        try
        {
            var previousSlug = GetSelectedModeSlug();
            SelectModeBySlug(card.ModeSlug);
            if (!string.Equals(previousSlug, card.ModeSlug, StringComparison.Ordinal))
            {
                try
                {
                    var configLoad = _configStore.Load();
                    var config = configLoad.Value ?? new Config();
                    config.LastMode = card.ModeSlug;
                    await _configStore.SaveAsync(config);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to save selected mode: {ex.Message}");
                }
                await RefreshStateAsync();
            }
            await HandleInstallAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"Mode card action error: {ex.Message}");
            SetMessage($"Помилка операції: {ex.Message}");
        }
    }


    private async void OnReleaseFeedCandidate(ReleasesResponse candidate)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnReleaseFeedCandidate(candidate));
            return;
        }

        try
        {
            if (_closing) return;
            await _feedCoordinator.OnCandidateAsync(candidate);
        }
        catch (Exception ex)
        {
            _logger.Error($"Feed candidate handler error: {ex.Message}");
        }
    }

    private async void OnReleaseFeedSuccess(ReleasesResponse feed)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnReleaseFeedSuccess(feed));
            return;
        }

        try
        {
            if (_closing)
                return;

            // A cached feed becomes authoritative only after a successful live
            // response has been accepted. Unchanged responses need no UI rebuild.
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
                RefreshKnownGamePatchPresentation();
                await RefreshStateAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Live feed success handler error: {ex.Message}");
        }
    }

    private async Task<bool> ApplyFeedPipelineAsync(ReleasesResponse candidate)
    {
        var generation = _gameSessionGeneration;
        var session = _activeGameSession;
        var cancellationToken = _gameSessionCts?.Token ?? default;
        if (!IsCurrentGameSession(generation, session)) return false;

        var previousSlug = GetSelectedModeSlug();

        _apiResponse = candidate;
        _apiLoadedSuccessfully = true;
        _apiErrorMessage = null;
        _apiErrorKind = ApiErrorKind.None;

        _suppressModeChanged = true;
        try
        {
            BuildDynamicModes();
            RestoreSelectionAfterFeedUpdate(previousSlug);
        }
        finally
        {
            _suppressModeChanged = false;
        }

        RefreshKnownGamePatchPresentation();
        await RefreshStateAsync(generation, cancellationToken);
        if (!IsCurrentGameSession(generation, session)) return false;
        _releaseFeedSource = ReleaseFeedSource.Live;
        _cachedFeedSavedAtUtc = null;
        await RefreshStateAsync(generation, cancellationToken);
        if (!IsCurrentGameSession(generation, session)) return false;
        await PersistLiveFeedAsync(candidate);
        if (!IsCurrentGameSession(generation, session)) return false;
        return true;
    }


    private void RestoreSelectionAfterFeedUpdate(string? previousSlug)
    {
        var allModes = _apiResponse?.Data?.Modes;
        var installable = DynamicModePolicy.GetInstallableModes(allModes);

        if (previousSlug != null && installable.Any(m =>
            string.Equals(m.Slug, previousSlug, StringComparison.Ordinal)))
        {
            SelectModeBySlug(previousSlug);
        }
        else
        {
            var fallback = DynamicModePolicy.ResolveInitialSelection(previousSlug, installable);
            if (fallback != null)
                SelectModeBySlug(fallback);
        }
    }


    private async Task RefreshStateAsync(long? expectedGeneration = null, CancellationToken cancellationToken = default)
    {
        if (_wwmSession != null)
        {
            await RefreshWwmStateAsync(expectedGeneration, cancellationToken).ConfigureAwait(true);
            return;
        }
        var generation = expectedGeneration ?? _gameSessionGeneration;
        var session = _activeGameSession;
        if (expectedGeneration.HasValue && !IsCurrentGameSession(generation, session))
            return;

        SetActionsEnabled(false);

        if (_gameRoot == null)
        {
            _lastResolvedState = LocalizationState.NotInstalled;
            ClearLocalFileTracking();
            if (!_apiLoadedSuccessfully)
                SetMessage(ApiErrorPresentation.GetUserMessage(_apiErrorKind, _apiErrorMessage));
            else if (_releaseFeedSource == ReleaseFeedSource.Cached)
                SetMessage(BuildCachedFeedMessage());
            else
                SetMessage("Гру не знайдено. Натисніть \"Знайти автоматично\" або оберіть папку.");
            ScheduleContentFit();
            return;
        }

        if (!_apiLoadedSuccessfully)
        {
            _lastResolvedState = LocalizationState.NotInstalled;
            SetMessage(ApiErrorPresentation.GetUserMessage(_apiErrorKind, _apiErrorMessage));
            SetActionsEnabled(!_operationInProgress);
            ScheduleContentFit();
            return;
        }

        // Resolve factual installed mode
        var installedLoad = _stateStore.Load();
        string? installedModeSlug = null;
        string? installedPublicId = null;

        if (installedLoad.Status == FileLoadStatus.Valid && installedLoad.Value?.Source == InstallationSource.Api)
        {
            installedModeSlug = installedLoad.Value.ModeSlug;
            installedPublicId = installedLoad.Value.PublicId;
        }

        // Factual LocalizationState uses INSTALLED mode's current
        CurrentRelease? installedModeCurrent = null;
        if (installedModeSlug != null)
        {
            var installedApiMode = _apiResponse?.Data?.Modes?
                .FirstOrDefault(m => string.Equals(m.Slug, installedModeSlug, StringComparison.Ordinal));
            installedModeCurrent = installedApiMode?.Current;
        }

        var gameLocPath = _gameDefinition.GetLocalizationFilePath(_gameRoot);
        LocalizationFileFingerprint.TryCapture(gameLocPath, out var capturedFingerprint, out var captureError);
        bool fingerprintCaptured = captureError == null;
        var stateResult = await _stateService.ResolveAsync(
            installedModeCurrent, gameLocPath, cancellationToken, gameRoot: _gameRoot);
        if (expectedGeneration.HasValue && !IsCurrentGameSession(generation, session))
            return;
        _lastResolvedState = stateResult.State;
        _lastInstalledModeSlug = installedModeSlug;
        _lastInstalledPublicId = installedPublicId;
        var selectedMode = GetSelectedApiMode();
        var selectedCurrent = selectedMode?.Current;

        var hasInstalledApiState = installedLoad.Status == FileLoadStatus.Valid
            && installedLoad.Value?.Source == InstallationSource.Api;
        var sameInstalledModeSelected = hasInstalledApiState
            && selectedMode?.Slug != null
            && string.Equals(installedModeSlug, selectedMode.Slug, StringComparison.Ordinal);

        // Installed marker on the matching mode card
        UpdateInstalledMarkers(installedModeSlug, installedPublicId);

        // Diagnostics
        string? diagnostic = stateResult.Error;

        var compatResult = _compatService.Check(selectedCurrent);
        if (diagnostic == null && !compatResult.IsAllowed && compatResult.Reason != null)
            diagnostic = compatResult.Reason;

        if (diagnostic == null && stateResult.State == LocalizationState.Corrupted)
            diagnostic = "Файл локалізації пошкоджено. Спробуйте встановити знову.";

        if (diagnostic == null && !IsCriticalHeadlineState(stateResult))
        {
            var policy = InstallActionPolicy.Evaluate(
                stateResult.State, installedModeSlug, installedPublicId,
                selectedMode, selectedCurrent, compatResult, _operationInProgress);

            if (policy.AlreadyInstalledExactTarget && stateResult.State == LocalizationState.UpToDate)
                diagnostic = "Встановлена остання доступна версія.";
            else if (!hasInstalledApiState && selectedCurrent != null)
                diagnostic = "Натисніть «Встановити», щоб встановити обраний режим.";
            else if (hasInstalledApiState && selectedCurrent != null)
                diagnostic = sameInstalledModeSelected
                    ? "Натисніть «Оновити», щоб оновити локалізацію."
                    : "Натисніть «Встановити», щоб перейти на обраний режим.";
        }

        // Ordinary card states explain themselves. The compact strip is reserved for
        // operations and important diagnostics that require global attention.
        if (_releaseFeedSource == ReleaseFeedSource.Cached)
        {
            var cachedMessage = BuildCachedFeedMessage();
            if (!string.IsNullOrWhiteSpace(diagnostic)
                && (IsCriticalHeadlineState(stateResult) || !compatResult.IsAllowed))
            {
                cachedMessage += $"{Environment.NewLine}{Environment.NewLine}{diagnostic}";
            }

            SetMessage(cachedMessage);
        }
        else
        {
            SetMessage(IsCriticalHeadlineState(stateResult) || !compatResult.IsAllowed
                ? diagnostic ?? ""
                : "");
        }

        // Action availability
        var actionPolicy = InstallActionPolicy.Evaluate(
            stateResult.State, installedModeSlug, installedPublicId,
            selectedMode, selectedCurrent, compatResult, _operationInProgress);

        SetActionsEnabled(actionPolicy.CanRestoreOriginal
            && _releaseFeedSource == ReleaseFeedSource.Live
            && AllowsLocalizationWriteActions());
        ApplyModeCardPresentations(stateResult.State, installedModeSlug, installedPublicId);
        ScheduleContentFit();

        if (fingerprintCaptured
            && installedLoad.Status == FileLoadStatus.Valid
            && installedLoad.Value?.Source == InstallationSource.Api)
        {
            _localFileChangeTracker.CommitResolved(gameLocPath, capturedFingerprint);
        }

        StartLocalFileMonitorIfEligible();
        ObserveLocalizationNotification(stateResult.State);
    }

    private string BuildCachedFeedMessage()
    {
        var savedAt = _cachedFeedSavedAtUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            ?? "невідомої дати";
        return $"Сервер недоступний. Показано останні збережені дані від {savedAt}."
            + $"{Environment.NewLine}{Environment.NewLine}Встановлення та оновлення стануть доступні після відновлення з'єднання.";
    }
}
