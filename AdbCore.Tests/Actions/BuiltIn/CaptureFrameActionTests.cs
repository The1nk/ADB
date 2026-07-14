using System;
using System.Linq;
using System.Threading.Tasks;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Tests.Screen;
using AdbCore.Tests.Targets;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class CaptureFrameActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = new FakeWindowHandle(handle) };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    [Fact]
    public async Task Capture_StoresNamedFrame()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { [FrameSourceConfig.FrameNameKey] = "hp" } };

        var result = await new CaptureFrameAction(new FakeWindowCapture(20, 10)).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.True(ctx.Frames.TryGet("hp", out var snap));
        Assert.Equal(20, snap!.Width);
        Assert.Equal(10, snap.Height);
    }

    [Fact]
    public async Task Capture_DefaultsFrameName_ToFrame()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id };

        await new CaptureFrameAction(new FakeWindowCapture(8, 8)).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(ctx.Frames.TryGet("frame", out _));
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var action = new BotAction();
        var result = await new CaptureFrameAction(new FakeWindowCapture(8, 8)).ExecuteAsync(Exec(action, new BotExecutionContext()), default);

        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new CaptureFrameAction(new FakeWindowCapture(8, 8));
        Assert.Equal("screen.captureFrame", def.TypeKey);
        Assert.Equal("Capture Frame", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.FrameNameKey);
    }
}
