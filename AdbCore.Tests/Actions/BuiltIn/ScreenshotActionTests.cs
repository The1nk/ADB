using System.Drawing;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ScreenshotActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    [Fact]
    public async Task SavesPng_OfClientArea()
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"adb-shot-{Guid.NewGuid():N}.png");
        try
        {
            var action = new ScreenshotAction(new FakeWindowCapture(120, 90));
            var a = new BotAction { TargetId = id, Config = { [ScreenshotAction.OutputPathKey] = path } };

            var result = await action.ExecuteAsync(Exec(a, WindowContext(id, (IntPtr)5)), default);

            Assert.True(result.Success);
            Assert.Equal("out", result.OutputPort);
            Assert.True(File.Exists(path));
            using var saved = Image.FromFile(path);
            Assert.Equal(120, saved.Width);
            Assert.Equal(90, saved.Height);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SavesPng_OfRegion_WhenRoiSet()
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"adb-shot-{Guid.NewGuid():N}.png");
        try
        {
            var action = new ScreenshotAction(new FakeWindowCapture(200, 150));
            var a = new BotAction { TargetId = id, Config =
            {
                [ScreenshotAction.OutputPathKey] = path,
                [ScreenActionBase.RegionXKey] = 10, [ScreenActionBase.RegionYKey] = 20,
                [ScreenActionBase.RegionWidthKey] = 50, [ScreenActionBase.RegionHeightKey] = 40,
            } };

            await action.ExecuteAsync(Exec(a, WindowContext(id, (IntPtr)5)), default);

            using var saved = Image.FromFile(path);
            Assert.Equal(50, saved.Width);
            Assert.Equal(40, saved.Height);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var action = new ScreenshotAction(new FakeWindowCapture(10, 10));
        var a = new BotAction { Config = { [ScreenshotAction.OutputPathKey] = "x.png" } };
        var result = await action.ExecuteAsync(Exec(a, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public async Task BlankPath_Fails()
    {
        var id = Guid.NewGuid();
        var action = new ScreenshotAction(new FakeWindowCapture(10, 10));
        var result = await action.ExecuteAsync(Exec(new BotAction { TargetId = id }, WindowContext(id, (IntPtr)5)), default);
        Assert.False(result.Success);
        Assert.Contains("output path", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new ScreenshotAction(new FakeWindowCapture(1, 1));
        Assert.Equal("screen.screenshot", def.TypeKey);
        Assert.Equal("Screenshot", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.False(def.SupportsRetry);
        Assert.Contains(def.ConfigFields, f => f.Key == ScreenshotAction.OutputPathKey);
        Assert.Contains(def.ConfigFields, f => f.Key == ScreenActionBase.RegionWidthKey);
    }
}
