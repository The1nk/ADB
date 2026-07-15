using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class BarMeasureCoreTests
{
    // Builds a 20x1 horizontal bar: first `filledPx` columns are `fill`, the rest `empty`.
    private static FrameSnapshot HBar(int width, int filledPx, Color fill, Color empty)
    {
        using var bmp = new Bitmap(width, 1, PixelFormat.Format32bppArgb);
        for (var x = 0; x < width; x++) { bmp.SetPixel(x, 0, x < filledPx ? fill : empty); }
        return FrameSnapshot.FromBitmap(bmp);
    }

    private static Dictionary<string, object> Config(string? fill, string? empty, string dir = "LeftToRight", int min = 0, int max = 15, int? tol = null)
    {
        var c = new Dictionary<string, object>
        {
            [TemplateMatchCore.RegionXKey] = 0,
            [TemplateMatchCore.RegionYKey] = 0,
            [TemplateMatchCore.RegionWidthKey] = 20,
            [TemplateMatchCore.RegionHeightKey] = 1,
            [BarMeasureCore.DirectionKey] = dir,
            [BarMeasureCore.MinValueKey] = min,
            [BarMeasureCore.MaxValueKey] = max,
        };
        if (fill is not null) { c[BarMeasureCore.FillColorKey] = fill; }
        if (empty is not null) { c[BarMeasureCore.EmptyColorKey] = empty; }
        if (tol is int t) { c[BarMeasureCore.ToleranceKey] = t; }
        return c;
    }

    [Fact]
    public void BothColors_HalfFilled_YieldsHalfOfRange()
    {
        var frame = HBar(20, 10, Color.Red, Color.Black);
        var r = BarMeasureCore.Measure(frame, Config("#FF0000", "#000000"));
        Assert.Equal(8, r.Value);           // round(0 + 0.5*15) = round(7.5) = 8 (away-from-zero)
        Assert.Equal(0.5, r.Fraction, 3);
    }

    [Fact]
    public void FillOnly_FullBar_YieldsMax()
    {
        var frame = HBar(20, 20, Color.Lime, Color.Black);
        var r = BarMeasureCore.Measure(frame, Config("#00FF00", null, tol: 40));
        Assert.Equal(15, r.Value);
        Assert.Equal(1.0, r.Fraction, 3);
    }

    [Fact]
    public void EmptyOnly_ClassifiesFillAsNotEmpty()
    {
        var frame = HBar(20, 5, Color.FromArgb(10, 200, 30), Color.Black);
        var r = BarMeasureCore.Measure(frame, Config(null, "#000000", tol: 40));
        Assert.Equal(0.25, r.Fraction, 3);  // 5/20
        Assert.Equal(4, r.Value);           // round(0.25*15)=round(3.75)=4
    }

    [Fact]
    public void RightToLeft_MeasuresFromRightEdge()
    {
        using var bmp = new Bitmap(20, 1, PixelFormat.Format32bppArgb);
        for (var x = 0; x < 20; x++) { bmp.SetPixel(x, 0, x >= 12 ? Color.Red : Color.Black); }
        var frame = FrameSnapshot.FromBitmap(bmp);
        var r = BarMeasureCore.Measure(frame, Config("#FF0000", "#000000", dir: "RightToLeft"));
        Assert.Equal(0.4, r.Fraction, 3);   // 8/20
    }

    [Fact]
    public void NoColors_Throws()
    {
        var frame = HBar(20, 10, Color.Red, Color.Black);
        Assert.Throws<ArgumentException>(() => BarMeasureCore.Measure(frame, Config(null, null)));
    }

    [Fact]
    public void NoRegion_Throws()
    {
        var frame = HBar(20, 10, Color.Red, Color.Black);
        var c = new Dictionary<string, object> { [BarMeasureCore.FillColorKey] = "#FF0000" };
        Assert.Throws<ArgumentException>(() => BarMeasureCore.Measure(frame, c));
    }

    [Fact]
    public void ParseColor_HandlesHashAndBareHex_AndRejectsGarbage()
    {
        Assert.Equal(Color.FromArgb(255, 0, 0).ToArgb(), BarMeasureCore.ParseColor("#FF0000")!.Value.ToArgb());
        Assert.Equal(Color.FromArgb(0, 255, 0).ToArgb(), BarMeasureCore.ParseColor("00FF00")!.Value.ToArgb());
        Assert.Null(BarMeasureCore.ParseColor(""));
        Assert.Null(BarMeasureCore.ParseColor("xyz"));
    }

    [Fact]
    public void Fields_ExposeExpectedKeys()
    {
        var keys = new List<string>();
        foreach (var f in BarMeasureCore.Fields()) { keys.Add(f.Key); }
        Assert.Contains(BarMeasureCore.FillColorKey, keys);
        Assert.Contains(BarMeasureCore.EmptyColorKey, keys);
        Assert.Contains(BarMeasureCore.DirectionKey, keys);
        Assert.Contains(BarMeasureCore.ResultVarKey, keys);
    }
}
