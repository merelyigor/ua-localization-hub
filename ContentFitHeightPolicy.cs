using System.Drawing;

namespace BdoClient;

internal static class ContentFitHeightPolicy
{
    public static Size CalculateTargetClientSize(
        Size currentClientSize,
        int preferredContentHeight,
        int minimumClientHeight,
        int maximumClientHeight)
    {
        var minimum = Math.Max(0, minimumClientHeight);
        var maximum = Math.Max(minimum, maximumClientHeight);
        var targetHeight = Math.Clamp(preferredContentHeight, minimum, maximum);
        return new Size(currentClientSize.Width, targetHeight);
    }
}
