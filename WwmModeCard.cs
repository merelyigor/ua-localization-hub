using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient;

internal sealed class WwmModeCard : UserControl
{
    private bool _selected;
    private bool _hovered;
    private bool _interactionEnabled = true;
    private WwmModeCardPresentation _presentation = new("", null, false, ModeCardTone.Neutral, false);
    private readonly Label _title;
    private readonly Label _description;
    private readonly Label _status;
    private readonly Label _releaseVersion;
    private readonly Button _action;
    private readonly Label _installedBadge;
    private readonly Font _installedBadgeFont;
    public WwmMode Mode { get; }
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }
    public string ModeSlug => Mode.Slug!;
    public string ModeVariant => Mode.Variant!;
    internal bool ActionEnabledForTest => _action.Enabled;
    internal bool ActionVisibleForTest => _action.Visible;
    internal string ActionTextForTest => _action.Text;
    internal Button ActionButtonForTest => _action;
    internal string StateTextForTest => _status.Text;
    internal bool InstalledBadgeVisibleForTest => _installedBadge.Visible;
    internal string InstalledBadgeTextForTest => _installedBadge.Text;
    internal Color InstalledBadgeForeColorForTest => _installedBadge.ForeColor;
    internal Color InstalledBadgeBackColorForTest => _installedBadge.BackColor;
    internal bool StatusLabelVisibleForTest => _status.Visible;
    internal bool ReleaseVersionVisibleForTest => _releaseVersion.Visible;
    internal string ReleaseVersionTextForTest => _releaseVersion.Text;
    internal Color ReleaseVersionForeColorForTest => _releaseVersion.ForeColor;
    internal bool IsInstalledForTest { get; private set; }
    public event EventHandler? SelectionRequested;
    public event EventHandler? ActionRequested;

    public WwmModeCard(WwmMode mode)
    {
        Mode = mode;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = mode.Label;
        TabStop = true;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        ForeColor = UiTheme.PrimaryText;
        MinimumSize = new Size(240, 220);
        Padding = new Padding(18);
        _title = new Label { Dock = DockStyle.Top, Height = 32, Text = mode.Label, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = UiTheme.PrimaryText, BackColor = Color.Transparent };
        _description = new Label { Dock = DockStyle.Fill, Text = JoinDescription(mode), ForeColor = UiTheme.SecondaryText, BackColor = Color.Transparent, AutoEllipsis = true };
        _releaseVersion = new Label
        {
            Dock = DockStyle.Bottom,
            Height = UiTheme.Scale(this, 24),
            ForeColor = UiTheme.SecondaryText,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            Visible = false,
            Margin = Padding.Empty
        };
        _status = new Label { Dock = DockStyle.Bottom, Height = 30, ForeColor = UiTheme.SecondaryText, BackColor = Color.Transparent };
        _action = new Button { Dock = DockStyle.Bottom, Height = 34, Visible = false, TabStop = true };
        _installedBadgeFont = new Font(Font.FontFamily, 8.5F, FontStyle.Bold);
        _installedBadge = new Label
        {
            AutoSize = true,
            BackColor = UiTheme.SuccessSurface,
            ForeColor = UiTheme.Success,
            Font = _installedBadgeFont,
            Padding = new Padding(UiTheme.Scale(this, 7), UiTheme.Scale(this, 3), UiTheme.Scale(this, 7), UiTheme.Scale(this, 3)),
            Text = "✓ Встановлено",
            TextAlign = ContentAlignment.MiddleCenter,
            UseMnemonic = false,
            Visible = false,
            Margin = Padding.Empty
        };
        _installedBadge.Paint += InstalledBadge_Paint;
        UiTheme.StyleAccentSecondaryButton(_action);
        _action.Click += (_, _) => ActionRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_description);
        Controls.Add(_releaseVersion);
        Controls.Add(_status);
        Controls.Add(_action);
        Controls.Add(_title);
        Controls.Add(_installedBadge);
        foreach (Control child in Controls)
        {
            if (!ReferenceEquals(child, _action))
                child.Click += (_, _) => SelectionRequested?.Invoke(this, EventArgs.Empty);
            child.MouseEnter += (_, _) => SetHovered(true);
            child.MouseLeave += (_, _) => UpdateHoverFromPointer();
        }
        MouseEnter += (_, _) => SetHovered(true);
        MouseLeave += (_, _) => UpdateHoverFromPointer();
    }

    public void Present(WwmModeCardPresentation presentation)
    {
        _presentation = presentation;
        _status.Text = presentation.StateText;
        _status.ForeColor = presentation.Tone switch
        {
            ModeCardTone.Success => UiTheme.Success,
            ModeCardTone.Warning => UiTheme.Accent,
            ModeCardTone.Error => UiTheme.Error,
            ModeCardTone.Busy => UiTheme.AccentSecondaryText,
            _ => UiTheme.SecondaryText
        };
        var showInstalledBadge = presentation.IsInstalled && presentation.Tone == ModeCardTone.Success;
        _installedBadge.Visible = showInstalledBadge;
        _status.Visible = !showInstalledBadge;
        var contentTop = showInstalledBadge
            ? UiTheme.Scale(this, 15) + _installedBadge.GetPreferredSize(Size.Empty).Height + UiTheme.Scale(this, 8)
            : 18;
        Padding = new Padding(18, contentTop, 18, 18);
        _action.Text = presentation.ActionText ?? "";
        _action.Visible = !string.IsNullOrWhiteSpace(presentation.ActionText);
        _action.Enabled = _interactionEnabled && presentation.ActionEnabled;
        IsInstalledForTest = presentation.IsInstalled;
        UiTheme.StyleCardActionButton(_action, presentation.Tone == ModeCardTone.Warning);
        Enabled = true;
        PerformLayout();
        if (showInstalledBadge)
            _installedBadge.BringToFront();
        Invalidate();
    }

    public void SetInteractionEnabled(bool enabled)
    {
        _interactionEnabled = enabled;
        Enabled = true;
        _action.Enabled = enabled && _presentation.ActionEnabled;
        Invalidate();
    }

    public void PresentBusy(string message)
    {
        Present(new WwmModeCardPresentation(message, null, false, ModeCardTone.Busy, false));
    }

    public void SetReleaseVersion(string? version)
    {
        _releaseVersion.Visible = !string.IsNullOrWhiteSpace(version);
        _releaseVersion.Text = string.IsNullOrWhiteSpace(version)
            ? string.Empty
            : $"Версія релізу: {version.Trim()}";
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_installedBadge != null && _installedBadge.Visible)
        {
            var padding = UiTheme.Scale(this, 18);
            _installedBadge.Location = new Point(
                ClientSize.Width - padding - _installedBadge.Width,
                padding - UiTheme.Scale(this, 3));
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(UiTheme.Background);
    }

    protected override void OnMouseEnter(EventArgs e) { SetHovered(true); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { UpdateHoverFromPointer(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus();
        SelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && (e.KeyCode is Keys.Space or Keys.Enter))
        {
            if (_action.Visible && _action.Enabled)
                ActionRequested?.Invoke(this, EventArgs.Empty);
            else
                SelectionRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ModeCardChrome.DrawSurface(e.Graphics, this, _presentation.Tone, IsSelected, _hovered, Enabled, Focused);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _installedBadgeFont.Dispose();
    }

    private void InstalledBadge_Paint(object? sender, PaintEventArgs e)
    {
        var bounds = _installedBadge.ClientRectangle;
        bounds.Width--;
        bounds.Height--;
        if (bounds.Width <= 1 || bounds.Height <= 1)
            return;
        ModeCardChrome.DrawBadgeOutline(e.Graphics, this, bounds, ModeCardTone.Success);
    }

    private void SetHovered(bool value)
    {
        if (_hovered == value) return;
        _hovered = value;
        Invalidate();
    }

    private void UpdateHoverFromPointer()
    {
        if (!IsHandleCreated || IsDisposed) return;
        SetHovered(ClientRectangle.Contains(PointToClient(Cursor.Position)));
    }

    private static string JoinDescription(WwmMode mode)
    {
        var pieces = new[] { mode.Description, mode.Audience }.Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join(Environment.NewLine + Environment.NewLine, pieces);
    }
}

internal sealed record WwmModeCardPresentation(
    string StateText, string? ActionText, bool ActionEnabled, ModeCardTone Tone, bool IsInstalled)
{
    public static WwmModeCardPresentation Create(
        bool sameInstalledMode,
        WwmInstalledStateKind installedState,
        bool stateTrusted,
        bool recoveryBlocked,
        bool gameDetected,
        bool operationInProgress)
    {
        var blocked = !stateTrusted || recoveryBlocked || installedState == WwmInstalledStateKind.Modified;
        if (blocked)
            return new(installedState == WwmInstalledStateKind.Modified ? "Файли змінені" : "Потрібне відновлення",
                null, false, installedState == WwmInstalledStateKind.Modified ? ModeCardTone.Error : ModeCardTone.Error, false);

        if (installedState == WwmInstalledStateKind.NeedsReinstall)
            return new("Файли гри змінено — потрібне повторне встановлення", "Встановити",
                gameDetected && !operationInProgress, ModeCardTone.Warning, false);

        if (sameInstalledMode && installedState == WwmInstalledStateKind.Current)
            return new("✓ Встановлено", null, false, ModeCardTone.Success, true);

        var isUpdate = sameInstalledMode && installedState == WwmInstalledStateKind.UpdateAvailable;
        return new(isUpdate ? "Доступне оновлення" : "Доступно",
            isUpdate ? "Оновити" : "Встановити",
            gameDetected && !operationInProgress,
            isUpdate ? ModeCardTone.Warning : ModeCardTone.Neutral,
            false);
    }
}
