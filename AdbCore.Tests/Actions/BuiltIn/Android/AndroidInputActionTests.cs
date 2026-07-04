using AdbCore.Actions;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Android;
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

    [Fact]
    public async Task LongPress_CallsDeviceWithCoordsAndDuration()
    {
        var action = new BotAction { Config = { ["x"] = 50, ["y"] = 60, ["durationMs"] = 900 } };
        var (ctx, dev) = WithDevice(action);

        var r = await new LongPressAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("onSuccess", r.OutputPort);
        Assert.Equal("longpress 50 60 900", dev.Calls.Single());
    }

    [Fact]
    public async Task LongPress_DefaultsDurationTo600()
    {
        var action = new BotAction { Config = { ["x"] = 1, ["y"] = 2 } };
        var (ctx, dev) = WithDevice(action);

        await new LongPressAction().ExecuteAsync(ctx, default);

        Assert.Equal("longpress 1 2 600", dev.Calls.Single());
    }

    [Fact]
    public async Task LongPress_NoDeviceBound_Fails()
    {
        var ctx = new BotExecutionContext();
        var exec = new ActionExecutionContext(new BotAction { Config = { ["x"] = 1, ["y"] = 1 } }, ctx, _ => { });

        var r = await new LongPressAction().ExecuteAsync(exec, default);

        Assert.False(r.Success);
        Assert.Contains("Android", r.ErrorMessage);
    }

    [Fact]
    public async Task SendText_CallsDeviceWithRawText()
    {
        var action = new BotAction { Config = { ["text"] = "hello world" } };
        var (ctx, dev) = WithDevice(action);

        var r = await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("text hello world", dev.Calls.Single());
    }

    [Fact]
    public async Task SendText_EmptyText_IsNoOpSuccess()
    {
        var action = new BotAction { Config = { ["text"] = "" } };
        var (ctx, dev) = WithDevice(action);

        var r = await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Empty(dev.Calls);
    }

    [Fact]
    public async Task SendText_SingleSpace_IsStillSent()
    {
        var action = new BotAction { Config = { ["text"] = " " } };
        var (ctx, dev) = WithDevice(action);

        await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.Equal("text  ", dev.Calls.Single()); // "text " + the space argument
    }

    [Fact]
    public async Task SendText_AdbKeyboard_WhenActive_BroadcastsUnicode()
    {
        var action = new BotAction { Config = { ["text"] = "!¹&!²", ["method"] = "ADB Keyboard" } };
        var (ctx, dev) = WithDevice(action);
        dev.ActiveIme = AndroidImes.AdbKeyboard; // Enable node already ran

        var r = await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("getime", dev.Calls[0]);
        Assert.Equal("adbkbtext !¹&!²", dev.Calls[1]);
    }

    [Fact]
    public async Task SendText_AdbKeyboard_WhenNotActive_FailsWithoutSending()
    {
        var action = new BotAction { Config = { ["text"] = "hi", ["method"] = "ADB Keyboard" } };
        var (ctx, dev) = WithDevice(action);
        dev.ActiveIme = "com.other/.Ime";

        var r = await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.False(r.Success);
        Assert.Contains("Enable ADB Keyboard", r.ErrorMessage);
        Assert.DoesNotContain(dev.Calls, c => c.StartsWith("adbkbtext"));
    }

    [Fact]
    public async Task SendText_DefaultMethod_UsesInputText()
    {
        var action = new BotAction { Config = { ["text"] = "hello" } }; // no method key
        var (ctx, dev) = WithDevice(action);

        await new SendTextAction().ExecuteAsync(ctx, default);

        Assert.Equal("text hello", dev.Calls.Single());
    }

    [Fact]
    public async Task PressKey_ResolvesNameAndRepeatsByCount()
    {
        var action = new BotAction { Config = { ["key"] = "Backspace", ["count"] = 3 } };
        var (ctx, dev) = WithDevice(action);

        var r = await new PressKeyAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("keyevent 67 3", dev.Calls.Single());
    }

    [Fact]
    public async Task PressKey_DefaultsToBackspaceCountOne()
    {
        var action = new BotAction(); // no config
        var (ctx, dev) = WithDevice(action);

        await new PressKeyAction().ExecuteAsync(ctx, default);

        Assert.Equal("keyevent 67 1", dev.Calls.Single());
    }

    [Fact]
    public async Task PressKey_CountBelowOne_ClampsToOne()
    {
        var action = new BotAction { Config = { ["key"] = "Enter", ["count"] = 0 } };
        var (ctx, dev) = WithDevice(action);

        await new PressKeyAction().ExecuteAsync(ctx, default);

        Assert.Equal("keyevent 66 1", dev.Calls.Single());
    }

    [Fact]
    public async Task PressKey_UnknownKey_Fails()
    {
        var action = new BotAction { Config = { ["key"] = "Meta", ["count"] = 1 } };
        var (ctx, dev) = WithDevice(action);

        var r = await new PressKeyAction().ExecuteAsync(ctx, default);

        Assert.False(r.Success);
        Assert.Contains("Meta", r.ErrorMessage);
        Assert.Empty(dev.Calls);
    }

    [Fact]
    public void PressKey_KeyField_OptionsComeFromAndroidKeyCodes()
    {
        var key = new PressKeyAction().ConfigFields.Single(f => f.Key == "key");
        Assert.Equal(ConfigFieldType.Enum, key.Type);
        Assert.Equal(AdbCore.Android.AndroidKeyCodes.Names, key.Options);
    }

    [Fact]
    public async Task EnableAdbKeyboard_StashesPreviousAndActivates()
    {
        var action = new BotAction { Config = { ["settleMs"] = 0 } }; // default previousImeVar = "PreviousIme"
        var (ctx, dev) = WithDevice(action);
        dev.ActiveIme = "com.original/.Ime";
        dev.AdbKeyboardInstalled = true;

        var r = await new EnableAdbKeyboardAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("com.original/.Ime", ctx.Context.Variables["PreviousIme"]);
        Assert.Contains($"imeenable {AndroidImes.AdbKeyboard}", dev.Calls);
        Assert.Contains($"imeset {AndroidImes.AdbKeyboard}", dev.Calls);
        Assert.Equal(AndroidImes.AdbKeyboard, dev.ActiveIme);
        // Verified activation by polling AFTER issuing the switch (not fire-and-forget):
        Assert.True(dev.Calls.IndexOf($"imeset {AndroidImes.AdbKeyboard}") < dev.Calls.LastIndexOf("getime"));
    }

    [Fact]
    public async Task EnableAdbKeyboard_TimesOut_WhenImeNeverActivates()
    {
        var action = new BotAction { Config = { ["settleMs"] = 0 } };
        var (ctx, dev) = WithDevice(action);
        dev.AdbKeyboardInstalled = true;
        dev.SuppressImeActivation = true; // ime set never "takes"

        var r = await new EnableAdbKeyboardAction(maxWaitMs: 50, pollIntervalMs: 5).ExecuteAsync(ctx, default);

        Assert.False(r.Success);
        Assert.Contains("did not become the active input method", r.ErrorMessage);
        // Prior IME is still stashed before the (failed) switch, so Restore can recover:
        Assert.Equal("com.original/.Ime", ctx.Context.Variables["PreviousIme"]);
    }

    [Fact]
    public async Task EnableAdbKeyboard_NotInstalled_FailsWithHint()
    {
        var action = new BotAction { Config = { ["settleMs"] = 0 } };
        var (ctx, dev) = WithDevice(action);
        dev.AdbKeyboardInstalled = false;

        var r = await new EnableAdbKeyboardAction().ExecuteAsync(ctx, default);

        Assert.False(r.Success);
        Assert.Contains("ADBKeyboard is not installed", r.ErrorMessage);
        Assert.DoesNotContain(dev.Calls, c => c.StartsWith("imeset"));
    }

    [Fact]
    public async Task EnableAdbKeyboard_NoDeviceBound_Fails()
    {
        var ctx = new BotExecutionContext();
        var exec = new ActionExecutionContext(new BotAction { Config = { ["settleMs"] = 0 } }, ctx, _ => { });

        var r = await new EnableAdbKeyboardAction().ExecuteAsync(exec, default);

        Assert.False(r.Success);
        Assert.Contains("Android", r.ErrorMessage);
    }

    [Fact]
    public async Task RestoreKeyboard_SetsImeFromVariable()
    {
        var action = new BotAction(); // default previousImeVar = "PreviousIme"
        var (ctx, dev) = WithDevice(action);
        ctx.Context.Variables["PreviousIme"] = "com.original/.Ime";

        var r = await new RestoreKeyboardAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("imeset com.original/.Ime", dev.Calls.Single());
    }

    [Fact]
    public async Task RestoreKeyboard_NoVariable_FailsClearly()
    {
        var action = new BotAction();
        var (ctx, dev) = WithDevice(action);

        var r = await new RestoreKeyboardAction().ExecuteAsync(ctx, default);

        Assert.False(r.Success);
        Assert.Contains("PreviousIme", r.ErrorMessage);
        Assert.Empty(dev.Calls);
    }
}
