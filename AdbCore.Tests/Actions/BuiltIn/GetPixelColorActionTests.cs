using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Targets;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class GetPixelColorActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = new FakeWindowHandle(handle) };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private sealed class SolidCapture : IWindowCapture
    {
        public int Calls { get; private set; }
        private readonly Color _color;
        public SolidCapture(Color color) => _color = color;
        public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method)
        {
            Calls++;
            var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(_color);
            return bmp;
        }
    }

    [Fact]
    public async Task Read_WritesColorVars_RoutesOut()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { [PixelReadCore.PointXKey] = 1, [PixelReadCore.PointYKey] = 2, [PixelReadCore.ResultVarKey] = "c" } };

        var result = await new GetPixelColorAction(new SolidCapture(Color.FromArgb(255, 12, 34, 56))).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Equal("#0C2238", ctx.Variables["cHex"]);
        Assert.Equal("12", ctx.Variables["cR"]);
        Assert.Equal("34", ctx.Variables["cG"]);
        Assert.Equal("56", ctx.Variables["cB"]);
    }

    [Fact]
    public async Task StoredSource_UsesFrame_NotFreshCapture()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        using (var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.FromArgb(255, 9, 9, 9)); }
            ctx.Frames.Set("f", FrameSnapshot.FromBitmap(bmp));
        }
        var action = new BotAction { TargetId = id, Config =
        {
            [PixelReadCore.PointXKey] = 0, [PixelReadCore.PointYKey] = 0,
            [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue, [FrameSourceConfig.FrameNameKey] = "f",
        } };
        var capture = new SolidCapture(Color.Red);

        var result = await new GetPixelColorAction(capture).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal(0, capture.Calls);
        Assert.Equal("#090909", ctx.Variables["pixelHex"]);
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var action = new BotAction { Config = { [PixelReadCore.PointXKey] = 0, [PixelReadCore.PointYKey] = 0 } };
        var result = await new GetPixelColorAction(new SolidCapture(Color.Red)).ExecuteAsync(Exec(action, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new GetPixelColorAction(new SolidCapture(Color.Red));
        Assert.Equal("screen.getPixelColor", def.TypeKey);
        Assert.Equal("Get Pixel Color", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == PixelReadCore.PointXKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
    }
}
