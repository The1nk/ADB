namespace BotBuilder.Core.Picker;

/// <summary>Turns two source-pixel corners into a top-left-origin rectangle with positive size,
/// clamped to the image bounds. Used by the ROI region picker.</summary>
public static class RegionSelection
{
    public static (int X, int Y, int Width, int Height) FromCorners(int x1, int y1, int x2, int y2, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp(Math.Min(x1, x2), 0, imageWidth);
        var top = Math.Clamp(Math.Min(y1, y2), 0, imageHeight);
        var right = Math.Clamp(Math.Max(x1, x2), 0, imageWidth);
        var bottom = Math.Clamp(Math.Max(y1, y2), 0, imageHeight);
        return (left, top, right - left, bottom - top);
    }
}
