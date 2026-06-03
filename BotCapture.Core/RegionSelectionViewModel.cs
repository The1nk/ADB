using System.Drawing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Holds the captured source image and the user's current selection rectangle (in source-image
/// pixels), and crops the selected region. The view maps drag coordinates into source pixels and assigns
/// <see cref="Selection"/>. Owns <see cref="Source"/>; <see cref="Crop"/> returns a new caller-owned bitmap.</summary>
public partial class RegionSelectionViewModel : ObservableObject, IDisposable
{
    public RegionSelectionViewModel(Bitmap source)
    {
        Source = source;
    }

    /// <summary>The full window capture being cropped from.</summary>
    public Bitmap Source { get; }

    /// <summary>Current selection in source-image pixels (may be un-normalized / out of bounds while
    /// dragging; <see cref="Crop"/> clamps it).</summary>
    [ObservableProperty] private Rectangle _selection;

    /// <summary>Normalizes a (possibly negative or overflowing) selection into an in-bounds pixel rect of
    /// at least 1×1.</summary>
    public static Rectangle ClampSelection(Rectangle sel, int width, int height)
    {
        var left = Math.Min(sel.Left, sel.Right);
        var top = Math.Min(sel.Top, sel.Bottom);
        var right = Math.Max(sel.Left, sel.Right);
        var bottom = Math.Max(sel.Top, sel.Bottom);

        left = Math.Clamp(left, 0, width - 1);
        top = Math.Clamp(top, 0, height - 1);
        right = Math.Clamp(right, 0, width);
        bottom = Math.Clamp(bottom, 0, height);

        var w = Math.Max(1, right - left);
        var h = Math.Max(1, bottom - top);
        return new Rectangle(left, top, w, h);
    }

    /// <summary>Crops the clamped <see cref="Selection"/> out of <see cref="Source"/> into a new bitmap.</summary>
    public Bitmap Crop()
    {
        var rect = ClampSelection(Selection, Source.Width, Source.Height);
        return Source.Clone(rect, Source.PixelFormat);
    }

    public void Dispose() => Source.Dispose();
}
