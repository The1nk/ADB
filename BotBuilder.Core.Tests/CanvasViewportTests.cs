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
}
