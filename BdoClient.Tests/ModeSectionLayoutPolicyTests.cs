using System.Drawing;

namespace BdoClient.Tests;

public sealed class ModeSectionLayoutPolicyTests
{
    [Fact]
    public void PlaceholderContentHeightIncludesPanelPaddingAndContentMargins()
    {
        var height = ModeSectionLayoutPolicy.CalculateContentPanelHeight(
            new Padding(0, 12, 0, 4),
            contentPreferredHeight: 22,
            new Padding(0, 3, 0, 5));

        Assert.Equal(46, height);
    }

    [Fact]
    public void PlaceholderSectionHeightIncludesCaptionAndCanReplacePreviouslyTallHeight()
    {
        var compactPanelHeight = ModeSectionLayoutPolicy.CalculateContentPanelHeight(
            new Padding(0, 12, 0, 0),
            contentPreferredHeight: 22,
            Padding.Empty);
        var compactSectionHeight = ModeSectionLayoutPolicy.CalculateSectionHeight(
            captionPreferredHeight: 28,
            captionMargin: new Padding(0, 0, 0, 4),
            compactPanelHeight);

        Assert.True(compactSectionHeight < 500);
        Assert.Equal(66, compactSectionHeight);
    }

    [Fact]
    public void PlaceholderHeightsNeverBecomeNegativeForUnusualMetrics()
    {
        var panelHeight = ModeSectionLayoutPolicy.CalculateContentPanelHeight(
            new Padding(-20),
            contentPreferredHeight: -5,
            new Padding(-10));
        var sectionHeight = ModeSectionLayoutPolicy.CalculateSectionHeight(
            captionPreferredHeight: -4,
            captionMargin: new Padding(-8),
            contentPanelHeight: -2);

        Assert.Equal(0, panelHeight);
        Assert.Equal(0, sectionHeight);
    }
}
