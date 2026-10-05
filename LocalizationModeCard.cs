using System.Drawing.Drawing2D;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient;

internal sealed class LocalizationModeCard : Control
{
    private readonly Button _actionButton;
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
        Controls.Add(_actionButton);
        MinimumSize = new Size(240, 220);
    }

    public void ApplyPresentation(ModeCardPresentation presentation)
    {
        _presentation = presentation;
        _actionButton.Text = presentation.ActionText ?? "";
        _actionButton.Visible = !string.IsNullOrWhiteSpace(presentation.ActionText);
        _actionButton.Enabled = presentation.ActionEnabled;
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
        var stateText = _presentation.StateText;
        height += _actionButton.Visible ? UiTheme.Scale(this, 32) + padding : string.IsNullOrWhiteSpace(stateText) ? padding : Measure(stateText, bodyFont, textWidth).Height + padding;
        return new Size(width, Math.Max(UiTheme.Scale(this, 208), height));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
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
    protected override void OnEnabledChanged(EventArgs e) { _actionButton.Enabled = Enabled && _presentation.ActionEnabled; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var bounds = ClientRectangle; bounds.Width--; bounds.Height--;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;
        var surface = !Enabled ? UiTheme.DisabledSurface : _hovered ? UiTheme.SurfaceHover : UiTheme.Surface;
        var borderColor = !Enabled ? UiTheme.DisabledBorder : _hovered ? UiTheme.BorderHover : UiTheme.Border;
        if (_presentation.Tone == ModeCardTone.Success) borderColor = UiTheme.Success;
        if (_presentation.Tone == ModeCardTone.Warning) borderColor = UiTheme.Accent;
        if (_presentation.Tone == ModeCardTone.Error) borderColor = UiTheme.Error;
        using var path = Rounded(bounds, UiTheme.Scale(this, 16));
        using var fill = new SolidBrush(surface);
        using var border = new Pen(borderColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);

        var padding = UiTheme.Scale(this, 18);
        var parsed = LocalizationFlagParser.Parse(DynamicModePolicy.GetDisplayName(Mode));
        DrawFlags(e.Graphics, parsed.CountryCodes, new Point(padding, padding), DeviceDpi / 96f);
        if (!string.IsNullOrWhiteSpace(_presentation.StateText))
        {
            using var badgeFont = new Font(Font.FontFamily, 8.5F, FontStyle.Bold);
            var badgeSize = TextRenderer.MeasureText(_presentation.StateText, badgeFont);
            var badgeRect = new Rectangle(Width - padding - badgeSize.Width - UiTheme.Scale(this, 14), padding - UiTheme.Scale(this, 3), badgeSize.Width + UiTheme.Scale(this, 14), UiTheme.Scale(this, 24));
            using var badgeFill = new SolidBrush(ToneSurface(_presentation.Tone));
            using var badgeBorder = new Pen(borderColor);
            using var badgePath = Rounded(badgeRect, UiTheme.Scale(this, 12));
            e.Graphics.FillPath(badgeFill, badgePath); e.Graphics.DrawPath(badgeBorder, badgePath);
            TextRenderer.DrawText(e.Graphics, _presentation.StateText, badgeFont, badgeRect, ToneText(_presentation.Tone), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        var top = padding + UiTheme.Scale(this, 30);
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
        if (Focused)
        {
            var focus = Rectangle.Inflate(bounds, -UiTheme.Scale(this, 5), -UiTheme.Scale(this, 5));
            using var focusPen = new Pen(UiTheme.SecondaryText, 1) { DashStyle = DashStyle.Dot };
            using var focusPath = Rounded(focus, UiTheme.Scale(this, 11)); e.Graphics.DrawPath(focusPen, focusPath);
        }
    }

    private static Size Measure(string text, Font font, int width) => TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    private static Color ToneSurface(ModeCardTone tone) => tone switch { ModeCardTone.Success => UiTheme.SuccessSurface, ModeCardTone.Warning => UiTheme.GoldSubtleSurface, ModeCardTone.Error => UiTheme.ErrorSurface, ModeCardTone.Busy => UiTheme.SurfaceHover, _ => UiTheme.SurfaceElevated };
    private static Color ToneText(ModeCardTone tone) => tone switch { ModeCardTone.Success => UiTheme.Success, ModeCardTone.Warning => UiTheme.Accent, ModeCardTone.Error => UiTheme.Error, _ => UiTheme.SecondaryText };
    private static GraphicsPath Rounded(Rectangle r, int radius) { var d = Math.Max(1, radius * 2); var path = new GraphicsPath(); path.AddArc(r.Left,r.Top,d,d,180,90); path.AddArc(r.Right-d,r.Top,d,d,270,90); path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.Left,r.Bottom-d,d,d,90,90); path.CloseFigure(); return path; }
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
