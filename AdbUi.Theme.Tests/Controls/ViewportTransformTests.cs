using AdbUi.Theme.Controls;

namespace AdbUi.Theme.Tests.Controls;

public class ViewportTransformTests
{
    [Fact]
    public void FitScale_WideSource_UsesWidthRatio()
    {
        // 1920x1080 into 900x700 -> min(900/1920, 700/1080) = 0.46875
        var scale = ViewportTransform.FitScale(900, 700, 1920, 1080);
        Assert.Equal(0.46875, scale, 5);
    }

    [Fact]
    public void FitScale_TallSource_UsesHeightRatio()
    {
        // 1080x1920 into 900x700 -> min(900/1080, 700/1920) = 0.364583...
        var scale = ViewportTransform.FitScale(900, 700, 1080, 1920);
        Assert.Equal(700.0 / 1920.0, scale, 5);
    }

    [Fact]
    public void FitScale_DegenerateInput_ReturnsOne()
    {
        Assert.Equal(1.0, ViewportTransform.FitScale(0, 700, 100, 100));
        Assert.Equal(1.0, ViewportTransform.FitScale(900, 700, 0, 100));
    }

    [Fact]
    public void ClampScale_HonorsBounds()
    {
        Assert.Equal(ViewportTransform.MaxScale, ViewportTransform.ClampScale(100));
        Assert.Equal(ViewportTransform.MinScale, ViewportTransform.ClampScale(0.0001));
        Assert.Equal(1.0, ViewportTransform.ClampScale(1.0));
    }

    [Fact]
    public void StepScale_AppliesGeometricFactorAndClamps()
    {
        Assert.Equal(1.2, ViewportTransform.StepScale(1.0, 1), 5);
        Assert.Equal(1.0 / 1.2, ViewportTransform.StepScale(1.0, -1), 5);
        Assert.Equal(ViewportTransform.MaxScale, ViewportTransform.StepScale(30, 5));
    }

    [Fact]
    public void ZoomToCursorOffset_KeepsSourcePixelUnderCursorFixed()
    {
        double oldScale = 1.0, newScale = 2.0, oldOffset = 40, cursor = 100;
        var newOffset = ViewportTransform.ZoomToCursorOffset(oldOffset, cursor, oldScale, newScale);

        var sourceBefore = (oldOffset + cursor) / oldScale;
        var sourceAfter = (newOffset + cursor) / newScale;
        Assert.Equal(sourceBefore, sourceAfter, 6);
    }

    [Fact]
    public void PointToSource_Inside_Maps()
    {
        var p = ViewportTransform.PointToSource(50, 50, 2.0, 100, 100);
        Assert.Equal((25, 25), p);
    }

    [Fact]
    public void PointToSource_Outside_ReturnsNull()
    {
        Assert.Null(ViewportTransform.PointToSource(-1, 10, 2.0, 100, 100));
        Assert.Null(ViewportTransform.PointToSource(50, 210, 2.0, 100, 100)); // 210/2=105 > 100
    }

    [Fact]
    public void PointToSource_RightEdge_ClampsToLastPixel()
    {
        // 200/2 = 100 == sourceWidth (edge inclusive) -> clamps to 99
        var p = ViewportTransform.PointToSource(200, 200, 2.0, 100, 100);
        Assert.Equal((99, 99), p);
    }

    [Fact]
    public void ClampToSource_AlwaysReturnsInBoundsPixel()
    {
        Assert.Equal((99, 99), ViewportTransform.ClampToSource(250, 250, 2.0, 100, 100));
        Assert.Equal((0, 0), ViewportTransform.ClampToSource(-10, -10, 2.0, 100, 100));
    }

    [Fact]
    public void SourceToDisplay_RoundTripsWithPointToSource()
    {
        var (dx, dy) = ViewportTransform.SourceToDisplay(25, 25, 2.0);
        Assert.Equal((50.0, 50.0), (dx, dy));
        Assert.Equal((25, 25), ViewportTransform.PointToSource(dx, dy, 2.0, 100, 100));
    }
}
