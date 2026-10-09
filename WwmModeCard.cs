using System.Drawing.Drawing2D;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient;

internal sealed class WwmModeCard : UserControl
{
    private readonly Label _title;
    private readonly Label _description;
    private readonly Label _status;
    private readonly Button _action;
    private readonly Label _installedBadge;
    private readonly Font _installedBadgeFont;
    public WwmMode Mode { get; }
    public bool IsSelected { get; set; }
    public string ModeSlug => Mode.Slug!;
    public string ModeVariant => Mode.Variant!;
    internal bool ActionEnabledForTest => _action.Enabled;
    internal bool ActionVisibleForTest => _action.Visible;
    internal string ActionTextForTest => _action.Text;
    internal string StateTextForTest => _status.Text;
    internal bool InstalledBadgeVisibleForTest => _installedBadge.Visible;
    internal string InstalledBadgeTextForTest => _installedBadge.Text;
    internal Color InstalledBadgeForeColorForTest => _installedBadge.ForeColor;
    internal Color InstalledBadgeBackColorForTest => _installedBadge.BackColor;
    internal bool StatusLabelVisibleForTest => _status.Visible;
    internal bool IsInstalledForTest { get; private set; }
    public event EventHandler? SelectionRequested;
    public event EventHandler? ActionRequested;

    public WwmModeCard(WwmMode mode)
    {
        Mode = mode;
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = mode.Label;
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.PrimaryText;
        MinimumSize = new Size(240, 220);
        Padding = new Padding(18);
        _title = new Label { Dock = DockStyle.Top, Height = 32, Text = mode.Label, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = UiTheme.PrimaryText };
        _description = new Label { Dock = DockStyle.Fill, Text = JoinDescription(mode), ForeColor = UiTheme.SecondaryText, AutoEllipsis = true };
        _status = new Label { Dock = DockStyle.Bottom, Height = 30, ForeColor = UiTheme.SecondaryText };
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
        Controls.Add(_status);
        Controls.Add(_action);
        Controls.Add(_title);
        Controls.Add(_installedBadge);
        Click += (_, _) => SelectionRequested?.Invoke(this, EventArgs.Empty);
        foreach (Control child in Controls) child.Click += (_, _) => SelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Present(WwmModeCardPresentation presentation)
    {
        _status.Text = presentation.StateText;
        _status.ForeColor = presentation.Tone == ModeCardTone.Success ? UiTheme.Success : UiTheme.SecondaryText;
        var showInstalledBadge = presentation.IsInstalled && presentation.Tone == ModeCardTone.Success;
        _installedBadge.Visible = showInstalledBadge;
        _status.Visible = !showInstalledBadge;
        var contentTop = showInstalledBadge
            ? UiTheme.Scale(this, 15) + _installedBadge.GetPreferredSize(Size.Empty).Height + UiTheme.Scale(this, 8)
            : 18;
        Padding = new Padding(18, contentTop, 18, 18);
        _action.Text = presentation.ActionText ?? "";
        _action.Visible = !string.IsNullOrWhiteSpace(presentation.ActionText);
        _action.Enabled = presentation.ActionEnabled;
        IsInstalledForTest = presentation.IsInstalled;
        UiTheme.StyleCardActionButton(_action, presentation.Tone == ModeCardTone.Warning);
        Enabled = true;
        PerformLayout();
        if (showInstalledBadge)
            _installedBadge.BringToFront();
        Invalidate();
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var tone = IsInstalledForTest ? ModeCardTone.Success : ModeCardTone.Neutral;
        using var pen = new Pen(tone == ModeCardTone.Success ? UiTheme.Success : IsSelected ? UiTheme.Accent : UiTheme.Border,
            tone == ModeCardTone.Success || IsSelected ? 2 : 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
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

        using var path = Rounded(bounds, UiTheme.Scale(this, 12));
        using var border = new Pen(UiTheme.Success);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(border, path);
    }

    private static GraphicsPath Rounded(Rectangle rectangle, int radius)
    {
        var diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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
