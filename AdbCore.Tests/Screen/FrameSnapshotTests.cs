using System;
using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Screen;

public class FrameSnapshotTests
{
    [Fact]
    public void FromBitmap_RoundTripsPixelColors()
    {
        using var bmp = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
        bmp.SetPixel(1, 0, Color.FromArgb(255, 40, 50, 60));
        bmp.SetPixel(0, 1, Color.FromArgb(255, 70, 80, 90));
        bmp.SetPixel(1, 1, Color.FromArgb(255, 100, 110, 120));

        var snap = FrameSnapshot.FromBitmap(bmp);

        Assert.Equal(2, snap.Width);
        Assert.Equal(2, snap.Height);
        Assert.Equal(Color.FromArgb(255, 10, 20, 30).ToArgb(), snap.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.FromArgb(255, 40, 50, 60).ToArgb(), snap.GetPixel(1, 0).ToArgb());
        Assert.Equal(Color.FromArgb(255, 100, 110, 120).ToArgb(), snap.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void ToBitmap_ReconstructsImage()
    {
        using var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.FromArgb(200, 11, 22, 33));

        var snap = FrameSnapshot.FromBitmap(bmp);
        using var rebuilt = snap.ToBitmap();

        Assert.Equal(bmp.GetPixel(0, 0).ToArgb(), rebuilt.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void GetPixel_OutOfRange_Throws()
    {
        using var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        var snap = FrameSnapshot.FromBitmap(bmp);
        Assert.Throws<ArgumentOutOfRangeException>(() => snap.GetPixel(1, 0));
    }

    [Fact]
    public void GetPixel_YOutOfRange_Throws()
    {
        using var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        var snap = FrameSnapshot.FromBitmap(bmp);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => snap.GetPixel(0, 1));
        Assert.Equal("y", ex.ParamName);
    }
}
