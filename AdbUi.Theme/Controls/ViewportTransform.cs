namespace AdbUi.Theme.Controls;

/// <summary>Pure geometry for a zoom/pan image viewport. The content is sized exactly to
/// <c>source × scale</c> (no letterbox), so mapping a point on the image to a source pixel is a plain
/// divide by scale. No <c>System.Windows</c> types, so it is fully unit-testable.</summary>
public static class ViewportTransform
{
    /// <summary>Smallest allowed zoom (5%).</summary>
    public const double MinScale = 0.05;

    /// <summary>Largest allowed zoom (32×).</summary>
    public const double MaxScale = 32.0;

    private const double WheelStepFactor = 1.2;

    /// <summary>The zoom that makes the whole source fit inside the viewport (min of the axis ratios),
    /// clamped to [<see cref="MinScale"/>, <see cref="MaxScale"/>]. Returns 1.0 for degenerate input.</summary>
    public static double FitScale(double viewportWidth, double viewportHeight, int sourceWidth, int sourceHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return 1.0;
        }

        var scale = Math.Min(viewportWidth / sourceWidth, viewportHeight / sourceHeight);
        return ClampScale(scale);
    }

    /// <summary>Clamps a zoom into [<see cref="MinScale"/>, <see cref="MaxScale"/>].</summary>
    public static double ClampScale(double scale) => Math.Clamp(scale, MinScale, MaxScale);

    /// <summary>Applies <paramref name="wheelTicks"/> geometric zoom steps (positive = in), then clamps.</summary>
    public static double StepScale(double scale, int wheelTicks)
        => ClampScale(scale * Math.Pow(WheelStepFactor, wheelTicks));

    /// <summary>The new scroll offset (one axis) that keeps the source pixel currently under the cursor
    /// fixed across a zoom change. Offsets/cursor are in display units; the caller's ScrollViewer clamps
    /// the result into its scrollable range.</summary>
    public static double ZoomToCursorOffset(double oldOffset, double viewportCursor, double oldScale, double newScale)
    {
        if (oldScale <= 0)
        {
            return 0;
        }

        return (oldOffset + viewportCursor) * (newScale / oldScale) - viewportCursor;
    }

    /// <summary>Maps a point on the <c>source × scale</c> image to a source pixel, or null when the point
    /// falls outside the image. The far/bottom edge is inclusive and clamps to the last pixel.</summary>
    public static (int X, int Y)? PointToSource(double pointX, double pointY, double scale, int sourceWidth, int sourceHeight)
    {
        if (scale <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return null;
        }

        var sx = pointX / scale;
        var sy = pointY / scale;
        if (sx < 0 || sy < 0 || sx > sourceWidth || sy > sourceHeight)
        {
            return null;
        }

        return (Math.Clamp((int)sx, 0, sourceWidth - 1), Math.Clamp((int)sy, 0, sourceHeight - 1));
    }

    /// <summary>Like <see cref="PointToSource"/> but always returns an in-bounds pixel (never null) — used
    /// while dragging so a drag past the edge clamps to the border.</summary>
    public static (int X, int Y) ClampToSource(double pointX, double pointY, double scale, int sourceWidth, int sourceHeight)
    {
        if (scale <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return (0, 0);
        }

        var sx = Math.Clamp((int)(pointX / scale), 0, sourceWidth - 1);
        var sy = Math.Clamp((int)(pointY / scale), 0, sourceHeight - 1);
        return (sx, sy);
    }

    /// <summary>Maps a source coordinate to a display position on the <c>source × scale</c> image.</summary>
    public static (double X, double Y) SourceToDisplay(double sourceX, double sourceY, double scale)
        => (sourceX * scale, sourceY * scale);
}
