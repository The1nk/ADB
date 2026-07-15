using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class PixelReadCoreTests
{
    private static FrameSnapshot TwoByTwo()
    {
        var bmp = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
        bmp.SetPixel(1, 0, Color.FromArgb(255, 40, 50, 60));
        bmp.SetPixel(0, 1, Color.FromArgb(255, 200, 100, 0));
        bmp.SetPixel(1, 1, Color.FromArgb(255, 255, 0, 128));
        using (bmp) { return FrameSnapshot.FromBitmap(bmp); }
    }

    private static Dictionary<string, object> Config(int x, int y, string? prefix = null)
    {
        var c = new Dictionary<string, object> { [PixelReadCore.PointXKey] = x, [PixelReadCore.PointYKey] = y };
        if (prefix is not null) { c[PixelReadCore.ResultVarKey] = prefix; }
        return c;
    }

    [Fact]
    public void ReadInto_WritesHexAndChannels_DefaultPrefix()
    {
        var frame = TwoByTwo();
        var vars = new Dictionary<string, object>();

        PixelReadCore.ReadInto(frame, Config(1, 1), vars);

        Assert.Equal("#FF0080", vars["pixelHex"]);
        Assert.Equal("255", vars["pixelR"]);
        Assert.Equal("0", vars["pixelG"]);
        Assert.Equal("128", vars["pixelB"]);
    }

    [Fact]
    public void ReadInto_CustomPrefix()
    {
        var frame = TwoByTwo();
        var vars = new Dictionary<string, object>();

        PixelReadCore.ReadInto(frame, Config(0, 1, "dot"), vars);

        Assert.Equal("#C86400", vars["dotHex"]); // (200,100,0)
        Assert.Equal("200", vars["dotR"]);
    }

    [Fact]
    public void ReadInto_OutOfRange_Throws()
    {
        var frame = TwoByTwo();
        var vars = new Dictionary<string, object>();
        Assert.Throws<ArgumentException>(() => PixelReadCore.ReadInto(frame, Config(2, 0), vars));
        Assert.Throws<ArgumentException>(() => PixelReadCore.ReadInto(frame, Config(0, -1), vars));
    }

    [Fact]
    public void Fields_ExposeXYAndResultVar()
    {
        var keys = new List<string>();
        foreach (var f in PixelReadCore.Fields()) { keys.Add(f.Key); }
        Assert.Equal(new[] { PixelReadCore.PointXKey, PixelReadCore.PointYKey, PixelReadCore.ResultVarKey }, keys);
    }
}
