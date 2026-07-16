namespace AdbUi.Theme.Controls;

/// <summary>A left-button pointer event on the image, mapped to source pixels.</summary>
public sealed class ImagePointerEventArgs : EventArgs
{
    public ImagePointerEventArgs(int sourceX, int sourceY, bool insideImage)
    {
        SourceX = sourceX;
        SourceY = sourceY;
        InsideImage = insideImage;
    }

    /// <summary>Source-pixel X (always in-bounds; clamped when the raw point was outside the image).</summary>
    public int SourceX { get; }

    /// <summary>Source-pixel Y (always in-bounds; clamped when the raw point was outside the image).</summary>
    public int SourceY { get; }

    /// <summary>True when the raw pointer position was inside the image (not the centered margin / past the edge).</summary>
    public bool InsideImage { get; }
}
