using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidCaptureFrameActionTests
{
    private static byte[] PngBytes(int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
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
    public async Task Capture_StoresDeviceFrame()
    {
        var id = Guid.NewGuid();
        var (ctx, dev) = DeviceContext(id, PngBytes(32, 16));
        var action = new BotAction { TargetId = id, Config = { [FrameSourceConfig.FrameNameKey] = "screen" } };

        var result = await new AndroidCaptureFrameAction().ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Contains("screenshot", dev.Calls);
        Assert.True(ctx.Frames.TryGet("screen", out var snap));
        Assert.Equal(32, snap!.Width);
        Assert.Equal(16, snap.Height);
    }

    [Fact]
    public async Task NoDevice_Fails()
    {
        var action = new BotAction();
        var result = await new AndroidCaptureFrameAction().ExecuteAsync(Exec(action, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("device", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new AndroidCaptureFrameAction();
        Assert.Equal("android.captureFrame", def.TypeKey);
        Assert.Equal("Android", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
    }
}
