using System.Drawing;
using System.IO;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class ThumbnailEncoderTests
{
    [Fact]
    public void ToPng_DownscalesLongSideToMaxDimension_PreservingAspect()
    {
        using var src = new Bitmap(200, 100);

        var bytes = ThumbnailEncoder.ToPng(src, 50);

        Assert.NotEmpty(bytes);
        using var decoded = new Bitmap(new MemoryStream(bytes));
        Assert.Equal(50, decoded.Width);
        Assert.Equal(25, decoded.Height);
    }

    [Fact]
    public void ToPng_NeverUpscales_SmallSourceUnchanged()
    {
        using var src = new Bitmap(30, 20);

        using var decoded = new Bitmap(new MemoryStream(ThumbnailEncoder.ToPng(src, 160)));

        Assert.Equal(30, decoded.Width);
        Assert.Equal(20, decoded.Height);
    }

    [Fact]
    public void ToPng_DownscalesPortrait_LongSideIsHeight()
    {
        using var src = new Bitmap(100, 200);

        var bytes = ThumbnailEncoder.ToPng(src, 50);

        using var decoded = new Bitmap(new MemoryStream(bytes));
        Assert.Equal(25, decoded.Width);
        Assert.Equal(50, decoded.Height);
    }
}
