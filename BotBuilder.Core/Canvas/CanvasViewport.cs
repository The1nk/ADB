using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Canvas;

/// <summary>The pan/zoom state of the editor canvas. Maps world coordinates to screen as
/// <c>screen = world * Scale + Offset</c>. Pure, view-framework-free.</summary>
public partial class CanvasViewport : ObservableObject
{
    public const double MinScale = 0.2;
    public const double MaxScale = 4.0;

    [ObservableProperty] private double _scale = 1.0;
    [ObservableProperty] private double _offsetX;
    [ObservableProperty] private double _offsetY;

    /// <summary>Pans by a screen-space delta.</summary>
    public void Pan(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
    }

    /// <summary>Zooms by <paramref name="factor"/> about the screen point
    /// (<paramref name="anchorX"/>, <paramref name="anchorY"/>), keeping the world point under
    /// that anchor fixed. Scale is clamped to [<see cref="MinScale"/>, <see cref="MaxScale"/>].</summary>
    public void ZoomAt(double anchorX, double anchorY, double factor)
    {
        var newScale = Math.Clamp(Scale * factor, MinScale, MaxScale);

        var worldX = (anchorX - OffsetX) / Scale;
        var worldY = (anchorY - OffsetY) / Scale;

        Scale = newScale;
        OffsetX = anchorX - worldX * newScale;
        OffsetY = anchorY - worldY * newScale;
    }

    /// <summary>Converts a screen-space point to world space — the inverse of
    /// <c>screen = world * Scale + Offset</c>. Use it to place content at a screen location
    /// (e.g. dropping a node at the center of the visible viewport) regardless of pan/zoom.</summary>
    public (double X, double Y) ScreenToWorld(double screenX, double screenY)
        => ((screenX - OffsetX) / Scale, (screenY - OffsetY) / Scale);
}
