using System.Drawing;

namespace BdoClient.Tests;

public sealed class ContentFitHeightPolicyTests
{
    [Fact]
    public void CalculateTargetClientSize_ShrinksWhenPreferredContentIsShorter()
    {
        var target = ContentFitHeightPolicy.CalculateTargetClientSize(
            new Size(960, 720), preferredContentHeight: 600, minimumClientHeight: 500, maximumClientHeight: 1000);

        Assert.Equal(new Size(960, 600), target);
    }

    [Fact]
    public void CalculateTargetClientSize_GrowsWhenPreferredContentIsTaller()
    {
        var target = ContentFitHeightPolicy.CalculateTargetClientSize(
            new Size(960, 600), preferredContentHeight: 800, minimumClientHeight: 500, maximumClientHeight: 1000);

        Assert.Equal(new Size(960, 800), target);
    }

    [Fact]
    public void CalculateTargetClientSize_ClampsToMinimum()
    {
        var target = ContentFitHeightPolicy.CalculateTargetClientSize(
            new Size(960, 720), preferredContentHeight: 300, minimumClientHeight: 520, maximumClientHeight: 1000);

        Assert.Equal(new Size(960, 520), target);
    }

    [Fact]
    public void CalculateTargetClientSize_ClampsToWorkingAreaMaximum()
    {
        var target = ContentFitHeightPolicy.CalculateTargetClientSize(
            new Size(960, 720), preferredContentHeight: 1400, minimumClientHeight: 500, maximumClientHeight: 900);

        Assert.Equal(new Size(960, 900), target);
    }

    [Fact]
    public void CalculateTargetClientSize_PreservesWidthAndCanShrinkAfterTallContent()
    {
        var afterContentShrink = ContentFitHeightPolicy.CalculateTargetClientSize(
            new Size(820, 1100), preferredContentHeight: 650, minimumClientHeight: 500, maximumClientHeight: 1200);

        Assert.Equal(820, afterContentShrink.Width);
        Assert.Equal(650, afterContentShrink.Height);
    }

    [Fact]
    public void CalculateTargetClientSize_ClampsUnusualBoundsToNonNegativeValidHeight()
    {
        var target = ContentFitHeightPolicy.CalculateTargetClientSize(
            new Size(760, 560), preferredContentHeight: -20, minimumClientHeight: -10, maximumClientHeight: -2);

        Assert.Equal(new Size(760, 0), target);
    }
}
