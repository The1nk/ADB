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

    /// <summary>Current zoom as an integer percentage (100 at <see cref="Scale"/> 1.0), for the zoom readout.</summary>
    public int ZoomPercent => (int)Math.Round(Scale * 100);

    partial void OnScaleChanged(double value) => OnPropertyChanged(nameof(ZoomPercent));

    /// <summary>Resets zoom to 100% about the centre of a viewport of the given size, keeping the world point
    /// under the centre fixed (un-zooms in place rather than jumping the pan).</summary>
    public void ResetZoom(double viewportWidth, double viewportHeight)
    {
        if (Scale <= 0)
        {
            return;
        }

        ZoomAt(viewportWidth / 2, viewportHeight / 2, 1.0 / Scale);
    }

    /// <summary>Frames world-space content bounds within a viewport, centring it with a margin so the whole
    /// graph is visible. No-op for empty/degenerate bounds or viewport.</summary>
    public void FitTo(double minX, double minY, double maxX, double maxY,
        double viewportWidth, double viewportHeight, double padding = 40)
    {
        var contentW = maxX - minX;
        var contentH = maxY - minY;
        if (contentW <= 0 || contentH <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        var availW = Math.Max(1, viewportWidth - 2 * padding);
        var availH = Math.Max(1, viewportHeight - 2 * padding);
        var scale = Math.Clamp(Math.Min(availW / contentW, availH / contentH), MinScale, MaxScale);

        var centreX = (minX + maxX) / 2;
        var centreY = (minY + maxY) / 2;

        Scale = scale;
        OffsetX = viewportWidth / 2 - centreX * scale;
        OffsetY = viewportHeight / 2 - centreY * scale;
    }
}
