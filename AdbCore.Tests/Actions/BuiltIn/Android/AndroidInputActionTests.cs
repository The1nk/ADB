using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidInputActionTests
{
    private static (ActionExecutionContext ctx, FakeAndroidDevice dev) WithDevice(BotAction action)
    {
        var dev = new FakeAndroidDevice();
        var ctx = new BotExecutionContext();
        var id = action.TargetId ?? Guid.NewGuid();
        action.TargetId = id;
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:x", Handle = dev };
        return (new ActionExecutionContext(action, ctx, _ => { }), dev);
    }

    [Fact]
    public async Task Tap_CallsDeviceWithCoords()
    {
        var action = new BotAction { Config = { ["x"] = 120, ["y"] = 240 } };
        var (ctx, dev) = WithDevice(action);

        var r = await new TapAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("onSuccess", r.OutputPort);
        Assert.Equal("tap 120 240", dev.Calls.Single());
    }

    [Fact]
    public async Task Swipe_CallsDeviceWithAllArgs()
    {
        var action = new BotAction { Config = { ["x1"] = 10, ["y1"] = 20, ["x2"] = 30, ["y2"] = 40, ["durationMs"] = 250 } };
        var (ctx, dev) = WithDevice(action);

        await new SwipeAction().ExecuteAsync(ctx, default);

        Assert.Equal("swipe 10 20 30 40 250", dev.Calls.Single());
    }

    [Fact]
    public async Task PressBack_CallsDevice()
    {
        var action = new BotAction();
        var (ctx, dev) = WithDevice(action);

        await new PressBackAction().ExecuteAsync(ctx, default);

        Assert.Equal("back", dev.Calls.Single());
    }

    [Fact]
    public async Task NoAndroidDeviceBound_Fails()
    {
        var ctx = new BotExecutionContext(); // no targets
        var exec = new ActionExecutionContext(new BotAction { Config = { ["x"] = 1, ["y"] = 1 } }, ctx, _ => { });

        var r = await new TapAction().ExecuteAsync(exec, default);

        Assert.False(r.Success);
        Assert.Contains("Android", r.ErrorMessage);
    }
}
