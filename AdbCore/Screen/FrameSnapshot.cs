using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AdbCore.Screen;

/// <summary>An immutable snapshot of a captured frame: a managed 32bpp BGRA pixel buffer with the source's
/// width/height. Being a managed array (not a live GDI <see cref="Bitmap"/>) makes it safe for concurrent
/// reads from parallel branches and frees it from disposal. Convert back to a <see cref="Bitmap"/> only when
/// a consumer (e.g. the template matcher) needs one.</summary>
public sealed class FrameSnapshot
{
    private readonly byte[] _bgra; // Width*Height*4, per pixel: B, G, R, A

    public int Width { get; }
    public int Height { get; }

    private FrameSnapshot(int width, int height, byte[] bgra)
    {
        Width = width;
        Height = height;
        _bgra = bgra;
    }

    /// <summary>Copies <paramref name="bitmap"/> into an immutable 32bpp BGRA snapshot (source format is
    /// normalized to 32bppArgb during the lock, so 24bpp Android PNGs and 32bpp window captures behave alike).</summary>
    public static FrameSnapshot FromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = bitmap.Width * 4;
            var buffer = new byte[rowBytes * bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, buffer, y * rowBytes, rowBytes);
            }
            return new FrameSnapshot(bitmap.Width, bitmap.Height, buffer);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>The color at <paramref name="x"/>,<paramref name="y"/>. Thread-safe (reads a shared byte[]).</summary>
    public Color GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Pixel ({x},{y}) is outside the {Width}x{Height} frame.");
        }
        var i = (y * Width + x) * 4;
        return Color.FromArgb(_bgra[i + 3], _bgra[i + 2], _bgra[i + 1], _bgra[i]); // A,R,G,B from B,G,R,A
    }

    /// <summary>Rebuilds a fresh 32bppArgb <see cref="Bitmap"/> from the snapshot. Caller disposes.</summary>
    public Bitmap ToBitmap()
    {
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = Width * 4;
            for (var y = 0; y < Height; y++)
            {
                Marshal.Copy(_bgra, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }
}
