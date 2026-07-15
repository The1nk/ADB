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

public class AndroidGetPixelColorActionTests
{
    private static byte[] SolidPng(Color color)
    {
        using var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(color); }
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

    [Fact]
    public async Task Read_WritesColorVars_RoutesOut()
    {
        var id = Guid.NewGuid();
        var (ctx, dev) = DeviceContext(id, SolidPng(Color.FromArgb(255, 12, 34, 56)));
        var action = new BotAction { TargetId = id, Config = { [PixelReadCore.PointXKey] = 1, [PixelReadCore.PointYKey] = 1, [PixelReadCore.ResultVarKey] = "c" } };

        var result = await new AndroidGetPixelColorAction().ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Contains("screenshot", dev.Calls);
        Assert.Equal("#0C2238", ctx.Variables["cHex"]);
    }

    [Fact]
    public async Task NoDevice_Fails()
    {
        var action = new BotAction { Config = { [PixelReadCore.PointXKey] = 0, [PixelReadCore.PointYKey] = 0 } };
        var result = await new AndroidGetPixelColorAction().ExecuteAsync(Exec(action, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("device", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new AndroidGetPixelColorAction();
        Assert.Equal("android.getPixelColor", def.TypeKey);
        Assert.Equal("Android", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == PixelReadCore.PointXKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
    }
}
