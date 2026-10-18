using System.Drawing.Drawing2D;

namespace BdoClient;

internal static class ModeCardChrome
{
    public static void DrawSurface(
        Graphics graphics,
        Control control,
        ModeCardTone tone,
        bool selected,
        bool hovered,
        bool enabled,
        bool focused)
    {
        var bounds = control.ClientRectangle;
        bounds.Width--;
        bounds.Height--;
        if (bounds.Width <= 1 || bounds.Height <= 1)
            return;

        var surface = !enabled
            ? UiTheme.DisabledSurface
            : selected
                ? hovered ? UiTheme.ModeSelectedHoverSurface : UiTheme.ModeSelectedSurface
                : hovered ? UiTheme.SurfaceHover : UiTheme.Surface;
        var borderColor = tone switch
        {
            ModeCardTone.Success => UiTheme.Success,
            ModeCardTone.Warning => UiTheme.Accent,
            ModeCardTone.Error => UiTheme.Error,
            ModeCardTone.Busy => UiTheme.AccentSecondaryText,
            _ when !enabled => UiTheme.DisabledBorder,
            _ when selected => UiTheme.Accent,
            _ when hovered => UiTheme.BorderHover,
            _ => UiTheme.Border
        };

        using var path = Rounded(bounds, UiTheme.Scale(control, 16));
        using var fill = new SolidBrush(surface);
        using var border = new Pen(borderColor, selected || tone == ModeCardTone.Success ? 2 : 1);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        if (focused)
        {
            var focus = Rectangle.Inflate(bounds, -UiTheme.Scale(control, 5), -UiTheme.Scale(control, 5));
            using var focusPen = new Pen(UiTheme.SecondaryText, 1) { DashStyle = DashStyle.Dot };
            using var focusPath = Rounded(focus, UiTheme.Scale(control, 11));
            graphics.DrawPath(focusPen, focusPath);
        }
    }

    public static Rectangle DrawTopRightBadge(
        Graphics graphics,
        Control control,
        string text,
        ModeCardTone tone,
        int inset = 18,
        int top = 15)
    {
        using var font = new Font(control.Font.FontFamily, 8.5F, FontStyle.Bold);
        var paddingX = UiTheme.Scale(control, 7);
        var badgeHeight = UiTheme.Scale(control, 24);
        var textSize = TextRenderer.MeasureText(text, font);
        var badge = new Rectangle(
            control.ClientSize.Width - UiTheme.Scale(control, inset) - textSize.Width - paddingX * 2,
            UiTheme.Scale(control, top),
            textSize.Width + paddingX * 2,
            badgeHeight);
        DrawBadge(graphics, control, badge, text, tone, font);
        return badge;
    }

    private static void DrawBadge(Graphics graphics, Control control, Rectangle bounds, string text, ModeCardTone tone, Font font)
    {
        var (surface, foreground, borderColor) = tone switch
        {
            ModeCardTone.Success => (UiTheme.SuccessSurface, UiTheme.Success, UiTheme.Success),
            ModeCardTone.Warning => (UiTheme.GoldSubtleSurface, UiTheme.Accent, UiTheme.Accent),
            ModeCardTone.Error => (UiTheme.ErrorSurface, UiTheme.Error, UiTheme.Error),
            ModeCardTone.Busy => (UiTheme.AccentSecondarySurface, UiTheme.AccentSecondaryText, UiTheme.AccentSecondaryBorder),
            _ => (UiTheme.SurfaceElevated, UiTheme.SecondaryText, UiTheme.Border)
        };

        using var fill = new SolidBrush(surface);
        using var path = Rounded(bounds, UiTheme.Scale(control, 12));
        graphics.FillPath(fill, path);
        DrawBadgeOutline(graphics, control, bounds, borderColor);
        TextRenderer.DrawText(graphics, text, font, bounds, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    public static void DrawBadgeOutline(Graphics graphics, Control control, Rectangle bounds, ModeCardTone tone)
    {
        var color = tone switch
        {
            ModeCardTone.Success => UiTheme.Success,
            ModeCardTone.Warning => UiTheme.Accent,
            ModeCardTone.Error => UiTheme.Error,
            ModeCardTone.Busy => UiTheme.AccentSecondaryBorder,
            _ => UiTheme.Border
        };
        DrawBadgeOutline(graphics, control, bounds, color);
    }

    private static void DrawBadgeOutline(Graphics graphics, Control control, Rectangle bounds, Color color)
    {
        using var path = Rounded(bounds, UiTheme.Scale(control, 12));
        using var border = new Pen(color);
        graphics.DrawPath(border, path);
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var diameter = Math.Min(Math.Max(1, radius * 2), Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
