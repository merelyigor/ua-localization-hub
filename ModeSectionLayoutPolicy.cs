using System.Drawing;

namespace BdoClient;

internal static class ModeSectionLayoutPolicy
{
    public static int CalculateContentPanelHeight(
        Padding panelPadding,
        int contentPreferredHeight,
        Padding contentMargin)
        => ClampHeight(
            (long)panelPadding.Vertical
            + Math.Max(0, contentPreferredHeight)
            + contentMargin.Vertical);

    public static int CalculateSectionHeight(
        int captionPreferredHeight,
        Padding captionMargin,
        int contentPanelHeight)
        => ClampHeight(
            (long)Math.Max(0, captionPreferredHeight)
            + captionMargin.Vertical
            + Math.Max(0, contentPanelHeight));

    private static int ClampHeight(long height)
        => (int)Math.Clamp(height, 0, int.MaxValue);
}
