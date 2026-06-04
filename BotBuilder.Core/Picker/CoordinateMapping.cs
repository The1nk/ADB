namespace BotBuilder.Core.Picker;

/// <summary>Pure geometry mapping a click within a <c>Stretch=Uniform</c> image area (display units) to a
/// source-pixel coordinate. Mirrors WPF's uniform (letterboxed) layout: the source is scaled by the
/// smaller of the width/height ratios and centered, so equal margins appear on the long axis.</summary>
public static class CoordinateMapping
{
    /// <summary>Returns the source pixel under a click, or null when the source/area is degenerate or the
    /// click falls in the letterbox margin (outside the rendered image). The result is clamped to the
    /// last in-bounds pixel ([0, sourceW-1] x [0, sourceH-1]).</summary>
    public static (int X, int Y)? ToSourcePixel(double clickX, double clickY, double areaWidth, double areaHeight, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || areaWidth <= 0 || areaHeight <= 0)
        {
            return null;
        }

        var scale = Math.Min(areaWidth / sourceWidth, areaHeight / sourceHeight);
        var renderedWidth = sourceWidth * scale;
        var renderedHeight = sourceHeight * scale;
        var offsetX = (areaWidth - renderedWidth) / 2;
        var offsetY = (areaHeight - renderedHeight) / 2;

        var localX = clickX - offsetX;
        var localY = clickY - offsetY;
        if (localX < 0 || localY < 0 || localX > renderedWidth || localY > renderedHeight)
        {
            return null;
        }

        var sourceX = Math.Clamp((int)(localX / scale), 0, sourceWidth - 1);
        var sourceY = Math.Clamp((int)(localY / scale), 0, sourceHeight - 1);
        return (sourceX, sourceY);
    }
}
