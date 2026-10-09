using BdoClient.Models;

namespace BdoClient;

internal sealed class WwmModeCard : UserControl
{
    private readonly Label _title;
    private readonly Label _description;
    private readonly Label _status;
    private readonly Button _action;
    public WwmMode Mode { get; }
    public bool IsSelected { get; set; }
    public string ModeSlug => Mode.Slug!;
    public string ModeVariant => Mode.Variant!;
    internal bool ActionEnabledForTest => _action.Enabled;
    internal bool ActionVisibleForTest => _action.Visible;
    internal string StateTextForTest => _status.Text;
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
        UiTheme.StyleAccentSecondaryButton(_action);
        _action.Click += (_, _) => ActionRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_description);
        Controls.Add(_status);
        Controls.Add(_action);
        Controls.Add(_title);
        Click += (_, _) => SelectionRequested?.Invoke(this, EventArgs.Empty);
        foreach (Control child in Controls) child.Click += (_, _) => SelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Present(string status, string? action, bool enabled)
    {
        _status.Text = status;
        _action.Text = action ?? "";
        _action.Visible = !string.IsNullOrWhiteSpace(action);
        _action.Enabled = enabled;
        Enabled = true;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(IsSelected ? UiTheme.Accent : UiTheme.Border, IsSelected ? 2 : 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private static string JoinDescription(WwmMode mode)
    {
        var pieces = new[] { mode.Description, mode.Audience }.Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join(Environment.NewLine + Environment.NewLine, pieces);
    }
}
