using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient;

internal sealed class LocalizationModeCard : Control
{
    private readonly Button _actionButton;
    private readonly Button _reinstallButton;
    private bool _hovered;
    private bool _selected;
    private bool _installed;
    private ModeCardPresentation _presentation = new(null, ModeCardTone.Neutral, null, false, false, false, null);

    public LocalizationMode Mode { get; }
    public string ModeSlug => Mode.Slug!;
    public event EventHandler? ActionRequested;
    public event EventHandler? SelectionRequested;
    public bool IsSelected { get => _selected; set { if (_selected != value) { _selected = value; Invalidate(); } } }
    public bool IsInstalled { get => _installed; set { if (_installed != value) { _installed = value; Invalidate(); } } }
    internal string? StateTextForTest => _presentation.StateText;
    internal string? ActionTextForTest => _presentation.ActionText;
    internal bool ActionEnabledForTest => _presentation.ActionEnabled;
    internal Button ActionButtonForTest => _actionButton;
    internal bool ReinstallVisibleForTest => _reinstallButton.Visible;
    internal bool ReinstallEnabledForTest => _reinstallButton.Enabled;
    internal string ReinstallTextForTest => _reinstallButton.Text;
    internal Button ReinstallButtonForTest => _reinstallButton;

    public LocalizationModeCard(LocalizationMode mode)
    {
        Mode = mode ?? throw new ArgumentNullException(nameof(mode));
        Tag = mode.Slug;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = DynamicModePolicy.GetDisplayName(mode);
        Margin = new Padding(0);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

        _actionButton = new Button { AutoSize = false, Height = 32, TabStop = true, AccessibleName = $"Дія для режиму {AccessibleName}", Margin = new Padding(0) };
        _actionButton.Click += (_, _) => ActionRequested?.Invoke(this, EventArgs.Empty);
        _reinstallButton = new Button
        {
            AutoSize = false,
            Size = new Size(UiTheme.Scale(this, 122), UiTheme.Scale(this, 24)),
            Text = "Перевстановити",
            AccessibleName = $"Перевстановити {AccessibleName}",
            Visible = false,
            TabStop = true,
            Margin = Padding.Empty
        };
        _reinstallButton.Click += (_, _) => ActionRequested?.Invoke(this, EventArgs.Empty);
        UiTheme.StyleSecondaryButton(_reinstallButton);
        Controls.Add(_actionButton);
        Controls.Add(_reinstallButton);
        MinimumSize = new Size(240, 220);
    }

    public void ApplyPresentation(ModeCardPresentation presentation)
    {
        _presentation = presentation;
        _actionButton.Text = presentation.ActionText ?? "";
        _actionButton.Visible = !string.IsNullOrWhiteSpace(presentation.ActionText);
        _actionButton.Enabled = presentation.ActionEnabled;
        _reinstallButton.Visible = presentation.ShowReinstall;
        _reinstallButton.Enabled = presentation.ReinstallEnabled;
        _actionButton.AccessibleDescription = presentation.StateText ?? "";
        UiTheme.StyleCardActionButton(_actionButton, presentation.Tone == ModeCardTone.Warning);
        var releaseLine = DynamicModePolicy.FormatReleaseLine(Mode);
        AccessibleDescription = string.IsNullOrWhiteSpace(presentation.StateText)
            ? $"{AccessibleName}. {releaseLine}"
            : $"{AccessibleName}. {presentation.StateText}. {releaseLine}";
        PerformLayout();
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = Math.Max(UiTheme.Scale(this, 240), proposedSize.Width > 0 ? proposedSize.Width : UiTheme.Scale(this, 300));
        var padding = UiTheme.Scale(this, 18);
        var textWidth = width - padding * 2;
        using var titleFont = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        using var bodyFont = new Font(Font.FontFamily, 9F, FontStyle.Regular);
        var parsed = LocalizationFlagParser.Parse(DynamicModePolicy.GetDisplayName(Mode));
        var height = padding + UiTheme.Scale(this, 24);
        height += Measure(parsed.Title, titleFont, textWidth).Height + UiTheme.Scale(this, 8);
        if (!string.IsNullOrWhiteSpace(Mode.Description))
            height += Measure(Mode.Description.Trim(), bodyFont, textWidth).Height + UiTheme.Scale(this, 8);
        height += Measure(DynamicModePolicy.FormatReleaseLine(Mode), bodyFont, textWidth).Height + UiTheme.Scale(this, 12);
        if (_reinstallButton.Visible)
            height += UiTheme.Scale(this, 30);
        var stateText = _presentation.StateText;
        height += _actionButton.Visible ? UiTheme.Scale(this, 32) + padding : string.IsNullOrWhiteSpace(stateText) ? padding : Measure(stateText, bodyFont, textWidth).Height + padding;
        return new Size(width, Math.Max(UiTheme.Scale(this, 208), height));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_reinstallButton.Visible)
            _reinstallButton.Bounds = new Rectangle(UiTheme.Scale(this, 18), UiTheme.Scale(this, 42), _reinstallButton.Width, _reinstallButton.Height);
        if (_actionButton.Visible)
        {
            var inset = UiTheme.Scale(this, 18);
            _actionButton.Bounds = new Rectangle(inset, Height - inset - _actionButton.Height, Math.Max(UiTheme.Scale(this, 112), Width - inset * 2), _actionButton.Height);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(UiTheme.Background);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Enabled) { Focus(); SelectionRequested?.Invoke(this, EventArgs.Empty); }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && _actionButton.Enabled && _actionButton.Visible)
        { ActionRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; e.SuppressKeyPress = true; return; }
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { _actionButton.Enabled = Enabled && _presentation.ActionEnabled; _reinstallButton.Enabled = Enabled && _presentation.ReinstallEnabled; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ModeCardChrome.DrawSurface(e.Graphics, this, _presentation.Tone, _selected, _hovered, Enabled, Focused);

        var padding = UiTheme.Scale(this, 18);
        var parsed = LocalizationFlagParser.Parse(DynamicModePolicy.GetDisplayName(Mode));
        DrawFlags(e.Graphics, parsed.CountryCodes, new Point(padding, padding), DeviceDpi / 96f);
        if (!string.IsNullOrWhiteSpace(_presentation.StateText))
        {
            ModeCardChrome.DrawTopRightBadge(e.Graphics, this, _presentation.StateText, _presentation.Tone,
                inset: 18, top: 15);
        }

        var top = padding + UiTheme.Scale(this, _reinstallButton.Visible ? 60 : 30);
        var textWidth = Width - padding * 2;
        using var titleFont = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        using var bodyFont = new Font(Font.FontFamily, 9F, FontStyle.Regular);
        TextRenderer.DrawText(e.Graphics, parsed.Title, titleFont, new Rectangle(padding, top, textWidth, Height - top), Enabled ? UiTheme.PrimaryText : UiTheme.DisabledText, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var y = top + Measure(parsed.Title, titleFont, textWidth).Height + UiTheme.Scale(this, 6);
        if (!string.IsNullOrWhiteSpace(Mode.Description))
        {
            var description = Mode.Description.Trim();
            TextRenderer.DrawText(e.Graphics, description, bodyFont, new Rectangle(padding, y, textWidth, Height - y), Enabled ? UiTheme.SecondaryText : UiTheme.DisabledText, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y += Measure(description, bodyFont, textWidth).Height + UiTheme.Scale(this, 6);
        }
        TextRenderer.DrawText(e.Graphics, DynamicModePolicy.FormatReleaseLine(Mode), bodyFont, new Rectangle(padding, y, textWidth, Height - y), UiTheme.SecondaryText, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    private static Size Measure(string text, Font font, int width) => TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    private static void DrawFlags(Graphics graphics, IReadOnlyList<string> codes, Point start, float scale)
    {
        var width=(int)Math.Round(30*scale); var height=(int)Math.Round(18*scale); var gap=(int)Math.Round(5*scale); var x=start.X;
        foreach(var code in codes)
        {
            var r=new Rectangle(x,start.Y,width,height); using var outline=new Pen(UiTheme.Border);
            if(code=="UA") { using var blue=new SolidBrush(Color.FromArgb(0,91,187)); using var yellow=new SolidBrush(Color.FromArgb(255,213,48)); graphics.FillRectangle(blue,r.Left,r.Top,r.Width,r.Height/2); graphics.FillRectangle(yellow,r.Left,r.Top+r.Height/2,r.Width,r.Height-r.Height/2); }
            else if(code=="GB") { using var blue=new SolidBrush(Color.FromArgb(1,33,105)); graphics.FillRectangle(blue,r); using var white=new Pen(Color.White,Math.Max(1,(int)(4*scale))); using var red=new Pen(Color.FromArgb(200,16,46),Math.Max(1,(int)(2*scale))); graphics.DrawLine(white,r.Left,r.Top,r.Right,r.Bottom);graphics.DrawLine(white,r.Right,r.Top,r.Left,r.Bottom);graphics.DrawLine(red,r.Left,r.Top,r.Right,r.Bottom);graphics.DrawLine(red,r.Right,r.Top,r.Left,r.Bottom);graphics.DrawLine(white,r.Left+r.Width/2,r.Top,r.Left+r.Width/2,r.Bottom);graphics.DrawLine(white,r.Left,r.Top+r.Height/2,r.Right,r.Top+r.Height/2);graphics.DrawLine(red,r.Left+r.Width/2,r.Top,r.Left+r.Width/2,r.Bottom);graphics.DrawLine(red,r.Left,r.Top+r.Height/2,r.Right,r.Top+r.Height/2); }
            else { using var fill=new SolidBrush(UiTheme.ControlBackground); graphics.FillRectangle(fill,r); TextRenderer.DrawText(graphics,code,SystemFonts.DefaultFont,r,UiTheme.SecondaryText,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter); }
            graphics.DrawRectangle(outline,r); x+=width+gap;
        }
    }
}
