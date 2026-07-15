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

public class MeasureBarActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = new FakeWindowHandle(handle) };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private sealed class HalfBarCapture : IWindowCapture
    {
        public int Calls { get; private set; }
        public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method)
        {
            Calls++;
            var bmp = new Bitmap(20, 1, PixelFormat.Format32bppArgb);
            for (var x = 0; x < 20; x++) { bmp.SetPixel(x, 0, x < 10 ? Color.Red : Color.Black); }
            return bmp;
        }
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
            [BarMeasureCore.ResultVarKey] = "hp",
        },
    };

    [Fact]
    public async Task Measure_WritesValueAndFraction_RoutesOut()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var result = await new MeasureBarAction(new HalfBarCapture()).ExecuteAsync(Exec(BarAction(id), ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Equal("8", ctx.Variables["hp"]);
        Assert.True(ctx.Variables.ContainsKey("hpFraction"));
    }

    [Fact]
    public async Task StoredSource_UsesFrame_NotFreshCapture()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        using (var bmp = new Bitmap(20, 1, PixelFormat.Format32bppArgb))
        {
            for (var x = 0; x < 20; x++) { bmp.SetPixel(x, 0, x < 15 ? Color.Red : Color.Black); }
            ctx.Frames.Set("f", FrameSnapshot.FromBitmap(bmp));
        }
        var action = BarAction(id);
        action.Config[FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue;
        action.Config[FrameSourceConfig.FrameNameKey] = "f";
        var capture = new HalfBarCapture();

        var result = await new MeasureBarAction(capture).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal(0, capture.Calls);
        Assert.Equal("11", ctx.Variables["hp"]);  // 15/20 = 0.75 -> round(11.25)=11
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var result = await new MeasureBarAction(new HalfBarCapture()).ExecuteAsync(Exec(BarAction(Guid.NewGuid()), new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new MeasureBarAction(new HalfBarCapture());
        Assert.Equal("screen.measureBar", def.TypeKey);
        Assert.Equal("Measure Bar", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == BarMeasureCore.FillColorKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
        Assert.Contains(def.ConfigFields, f => f.Key == TemplateMatchCore.RegionWidthKey);
    }
}
