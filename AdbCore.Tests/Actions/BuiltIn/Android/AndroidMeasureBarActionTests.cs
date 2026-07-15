using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidMeasureBarActionTests
{
    private static byte[] HalfBarPng()
    {
        using var bmp = new Bitmap(20, 1, PixelFormat.Format32bppArgb);
        for (var x = 0; x < 20; x++) { bmp.SetPixel(x, 0, x < 10 ? Color.Red : Color.Black); }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static (BotExecutionContext ctx, FakeAndroidDevice dev) DeviceContext(Guid id, byte[] png)
    {
        var dev = new FakeAndroidDevice { ScreenshotBytes = png };
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:x", Handle = dev };
        return (ctx, dev);
    }

    private static BotAction BarAction(Guid id) => new()
    {
        TargetId = id,
        Config =
        {
            [BarMeasureCore.FillColorKey] = "#FF0000",
            [BarMeasureCore.EmptyColorKey] = "#000000",
            [TemplateMatchCore.RegionXKey] = 0,
            [TemplateMatchCore.RegionYKey] = 0,
            [TemplateMatchCore.RegionWidthKey] = 20,
            [TemplateMatchCore.RegionHeightKey] = 1,
            [BarMeasureCore.ResultVarKey] = "atk",
        },
    };

    [Fact]
    public async Task Measure_WritesValueAndFraction_RoutesOut()
    {
        var id = Guid.NewGuid();
        var (ctx, dev) = DeviceContext(id, HalfBarPng());
        var result = await new AndroidMeasureBarAction().ExecuteAsync(Exec(BarAction(id), ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Contains("screenshot", dev.Calls);
        Assert.Equal("8", ctx.Variables["atk"]);
        Assert.True(ctx.Variables.ContainsKey("atkFraction"));
    }

    [Fact]
    public async Task NoDevice_Fails()
    {
        var result = await new AndroidMeasureBarAction().ExecuteAsync(Exec(BarAction(Guid.NewGuid()), new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("device", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new AndroidMeasureBarAction();
        Assert.Equal("android.measureBar", def.TypeKey);
        Assert.Equal("Android", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == BarMeasureCore.FillColorKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
    }
}
