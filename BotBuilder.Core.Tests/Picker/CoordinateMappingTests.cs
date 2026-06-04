using BotBuilder.Core.Picker;
using Xunit;

namespace BotBuilder.Core.Tests.Picker;

public class CoordinateMappingTests
{
    [Fact]
    public void NoLetterbox_SameAspect_MapsProportionally()
    {
        // 200x100 display showing a 400x200 source (exact 0.5 scale, no letterbox).
        var p = CoordinateMapping.ToSourcePixel(100, 50, 200, 100, 400, 200);
        Assert.Equal((200, 100), p);
    }

    [Fact]
    public void Letterboxed_WiderDisplay_AccountsForHorizontalMargin()
    {
        // Source 100x100 shown in a 300x100 area: uniform scale = 1.0, rendered 100x100 centered → x-offset 100.
        // A click at display (150,50) is the center of the rendered image → source (50,50).
        var p = CoordinateMapping.ToSourcePixel(150, 50, 300, 100, 100, 100);
        Assert.Equal((50, 50), p);
    }

    [Fact]
    public void ClickInLetterboxMargin_ReturnsNull()
    {
        // Same 300x100 area / 100x100 source: x=10 is in the left margin (image starts at x=100).
        Assert.Null(CoordinateMapping.ToSourcePixel(10, 50, 300, 100, 100, 100));
    }

    [Fact]
    public void ClickAtFarEdge_ClampsToLastPixel()
    {
        // Bottom-right corner of a 100x100 source shown 1:1; click exactly at the edge maps to (99,99), not 100.
        var p = CoordinateMapping.ToSourcePixel(100, 100, 100, 100, 100, 100);
        Assert.Equal((99, 99), p);
    }

    [Theory]
    [InlineData(0, 0, 100, 100)]
    [InlineData(100, 100, 0, 0)]
    public void DegenerateSizes_ReturnNull(double areaW, double areaH, int srcW, int srcH)
    {
        Assert.Null(CoordinateMapping.ToSourcePixel(5, 5, areaW, areaH, srcW, srcH));
    }
}
