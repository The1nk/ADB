using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace BotCapture.Core;

/// <summary>Encodes a capture into a small PNG thumbnail for list display. Pure (bytes in/out, no UI
/// dependency) so the view can turn the bytes into a WPF image.</summary>
public static class ThumbnailEncoder
{
    /// <summary>Downscales <paramref name="source"/> to fit within <paramref name="maxDimension"/> px on
    /// its longest side (preserving aspect ratio; never upscaling) and returns PNG-encoded bytes.</summary>
    public static byte[] ToPng(Bitmap source, int maxDimension)
    {
        var longest = Math.Max(source.Width, source.Height);
        var scale = Math.Min(1.0, maxDimension / (double)longest);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var thumb = new Bitmap(width, height);
        using (var g = Graphics.FromImage(thumb))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, width, height);
        }

        using var stream = new MemoryStream();
        thumb.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
