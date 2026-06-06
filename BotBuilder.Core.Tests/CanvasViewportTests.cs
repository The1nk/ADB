using BotBuilder.Core.Canvas;
using Xunit;

namespace BotBuilder.Core.Tests;

public class CanvasViewportTests
{
    [Fact]
    public void Defaults_AreIdentity()
    {
        var v = new CanvasViewport();

        Assert.Equal(1.0, v.Scale);
        Assert.Equal(0.0, v.OffsetX);
        Assert.Equal(0.0, v.OffsetY);
    }

    [Fact]
    public void Pan_AddsToOffset()
    {
        var v = new CanvasViewport();

        v.Pan(15, -20);

        Assert.Equal(15, v.OffsetX);
        Assert.Equal(-20, v.OffsetY);
    }

    [Fact]
    public void ZoomAt_KeepsWorldPointUnderAnchorFixed()
    {
        var v = new CanvasViewport();
        v.Pan(30, 10);

        const double anchorX = 200, anchorY = 120;
        var worldBeforeX = (anchorX - v.OffsetX) / v.Scale;
        var worldBeforeY = (anchorY - v.OffsetY) / v.Scale;

        v.ZoomAt(anchorX, anchorY, 1.25);

        var worldAfterX = (anchorX - v.OffsetX) / v.Scale;
        var worldAfterY = (anchorY - v.OffsetY) / v.Scale;

        Assert.Equal(worldBeforeX, worldAfterX, 6);
        Assert.Equal(worldBeforeY, worldAfterY, 6);
        Assert.Equal(1.25, v.Scale, 6);
    }

    [Fact]
    public void ZoomAt_ClampsToMaxScale()
    {
        var v = new CanvasViewport();

        for (var i = 0; i < 50; i++)
        {
            v.ZoomAt(0, 0, 2.0);
        }

        Assert.Equal(CanvasViewport.MaxScale, v.Scale, 6);
    }

    [Fact]
    public void ZoomAt_ClampsToMinScale()
    {
        var v = new CanvasViewport();

        for (var i = 0; i < 50; i++)
        {
            v.ZoomAt(0, 0, 0.5);
        }

        Assert.Equal(CanvasViewport.MinScale, v.Scale, 6);
    }

    [Fact]
    public void ScreenToWorld_Identity_ReturnsSamePoint()
    {
        var v = new CanvasViewport();

        var (x, y) = v.ScreenToWorld(100, 50);

        Assert.Equal(100, x, 6);
        Assert.Equal(50, y, 6);
    }

    [Fact]
    public void ScreenToWorld_WithPan_SubtractsOffset()
    {
        var v = new CanvasViewport();
        v.Pan(30, -20);

        var (x, y) = v.ScreenToWorld(100, 50);

        Assert.Equal(70, x, 6);
        Assert.Equal(70, y, 6);
    }

    [Fact]
    public void ScreenToWorld_WithZoom_DividesByScale()
    {
        var v = new CanvasViewport();
        v.ZoomAt(0, 0, 2.0); // Scale = 2, Offset stays 0

        var (x, y) = v.ScreenToWorld(100, 50);

        Assert.Equal(50, x, 6);
        Assert.Equal(25, y, 6);
    }

    [Fact]
    public void ScreenToWorld_IsInverseOfWorldToScreenTransform()
    {
        var v = new CanvasViewport();
        v.Pan(40, 15);
        v.ZoomAt(200, 120, 1.5);

        const double worldX = 333, worldY = -77;
        var screenX = worldX * v.Scale + v.OffsetX;
        var screenY = worldY * v.Scale + v.OffsetY;

        var (x, y) = v.ScreenToWorld(screenX, screenY);

        Assert.Equal(worldX, x, 6);
        Assert.Equal(worldY, y, 6);
    }

    [Theory]
    [InlineData(1.0, 100)]
    [InlineData(0.5, 50)]
    [InlineData(2.0, 200)]
    [InlineData(1.23, 123)]
    public void ZoomPercent_ReflectsScale(double scale, int expected)
    {
        var v = new CanvasViewport { Scale = scale };

        Assert.Equal(expected, v.ZoomPercent);
    }

    [Fact]
    public void ResetZoom_SetsScaleTo1_KeepingCentreFixed()
    {
        var v = new CanvasViewport();
        v.Pan(40, 15);
        v.ZoomAt(0, 0, 2.5);

        const double vw = 1000, vh = 600;
        var (worldCx, worldCy) = v.ScreenToWorld(vw / 2, vh / 2);

        v.ResetZoom(vw, vh);

        Assert.Equal(1.0, v.Scale, 6);
        var (afterCx, afterCy) = v.ScreenToWorld(vw / 2, vh / 2);
        Assert.Equal(worldCx, afterCx, 6); // same world point still under the viewport centre
        Assert.Equal(worldCy, afterCy, 6);
    }

    [Fact]
    public void FitTo_CentresContentAndClampsScale()
    {
        var v = new CanvasViewport();

        v.FitTo(0, 0, 200, 100, viewportWidth: 1000, viewportHeight: 600, padding: 40);

        // Content fits with room to spare, so scale clamps to MaxScale.
        Assert.Equal(CanvasViewport.MaxScale, v.Scale, 6);
        // The content centre (100, 50) maps to the viewport centre (500, 300).
        Assert.Equal(500, 100 * v.Scale + v.OffsetX, 6);
        Assert.Equal(300, 50 * v.Scale + v.OffsetY, 6);
    }

    [Fact]
    public void FitTo_ScalesDownContentLargerThanViewport()
    {
        var v = new CanvasViewport();

        v.FitTo(0, 0, 4000, 2000, viewportWidth: 1000, viewportHeight: 600, padding: 40);

        Assert.True(v.Scale < 1.0);
        Assert.True(v.Scale >= CanvasViewport.MinScale);
        // Content centre (2000, 1000) still lands at the viewport centre.
        Assert.Equal(500, 2000 * v.Scale + v.OffsetX, 6);
        Assert.Equal(300, 1000 * v.Scale + v.OffsetY, 6);
    }

    [Fact]
    public void FitTo_EmptyBounds_IsNoOp()
    {
        var v = new CanvasViewport();
        v.ZoomAt(0, 0, 1.5);
        var scaleBefore = v.Scale;

        v.FitTo(100, 100, 100, 100, viewportWidth: 1000, viewportHeight: 600);

        Assert.Equal(scaleBefore, v.Scale, 6);
    }
}
