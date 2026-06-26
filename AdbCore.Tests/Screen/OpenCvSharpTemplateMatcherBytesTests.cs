using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Screen;

public class OpenCvSharpTemplateMatcherBytesTests
{
    private static byte[] PngOf(Color fill, int w, int h)
    {
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(fill); }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public void Match_FromBytes_FindsTemplateInHaystack()
    {
        using var haystack = new Bitmap(40, 30, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(haystack))
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Black, 8, 6, 10, 10);
        }
        var templatePng = PngOf(Color.Black, 10, 10);

        var hit = new OpenCvSharpTemplateMatcher().Match(haystack, templatePng, 0.5);

        Assert.True(hit.HasValue);
        Assert.Equal(10, hit!.Value.Width);
        Assert.Equal(10, hit.Value.Height);
    }

    [Fact]
    public void Match_FromEmptyBytes_Throws()
    {
        using var haystack = new Bitmap(10, 10);
        Assert.ThrowsAny<Exception>(() => new OpenCvSharpTemplateMatcher().Match(haystack, Array.Empty<byte>(), 0.5));
    }
}
