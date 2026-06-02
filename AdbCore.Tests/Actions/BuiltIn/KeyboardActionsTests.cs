using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;
using AdbCore.Tests.Input;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class KeyboardActionsTests
{
    private sealed class Senders
    {
        public RecordingInputSender SendInput { get; } = new();
        public RecordingInputSender PostMessage { get; } = new();
        public InputSenderResolver Resolver() => new(SendInput, PostMessage);
    }

    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction action, BotExecutionContext ctx) => new(action, ctx, _ => { });

    [Fact]
    public async Task TypeText_SendsTextToTarget_ViaSendInput_Default()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[TypeTextAction.TextKey] = "hello";

        var result = await new TypeTextAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)5)), default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal("TypeText", senders.SendInput.LastOp);
        Assert.Equal("hello", senders.SendInput.LastText);
        Assert.Equal((IntPtr)5, senders.SendInput.LastWindow);
    }

    [Fact]
    public async Task TypeText_PostMessageMethod_RoutesToPostMessageSender()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[TypeTextAction.TextKey] = "hi";
        action.Config[InputActionBase.MethodKey] = InputSenderResolver.PostMessageMethod;

        await new TypeTextAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)6)), default);

        Assert.Equal("hi", senders.PostMessage.LastText);
        Assert.Equal(0, senders.SendInput.Calls);
    }

    [Fact]
    public async Task TypeText_NoTarget_FailsWithoutSending()
    {
        var senders = new Senders();
        var action = new BotAction { TargetId = null };
        action.Config[TypeTextAction.TextKey] = "x";

        var result = await new TypeTextAction(senders.Resolver()).ExecuteAsync(Exec(action, new BotExecutionContext()), default);

        Assert.False(result.Success);
        Assert.Equal(0, senders.SendInput.Calls);
        Assert.Contains("Window target", result.ErrorMessage);
    }

    [Fact]
    public async Task TypeText_EmptyText_Succeeds()
    {
        // Empty text is intentionally benign (like Log / Set Variable) — the action succeeds; the Win32
        // sender skips it without stealing focus.
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[TypeTextAction.TextKey] = "";

        var result = await new TypeTextAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)4)), default);

        Assert.True(result.Success);
        Assert.Equal("", senders.SendInput.LastText);
    }

    [Fact]
    public async Task KeyPress_ResolvesKeyAndModifiers()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[KeyPressAction.KeyKey] = "C";
        action.Config[KeyPressAction.CtrlKey] = true;
        action.Config[KeyPressAction.ShiftKey] = true;

        var result = await new KeyPressAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)7)), default);

        Assert.True(result.Success);
        Assert.Equal("KeyPress", senders.SendInput.LastOp);
        Assert.Equal((ushort)0x43, senders.SendInput.LastVk);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, senders.SendInput.LastModifiers);
    }

    [Fact]
    public async Task KeyPress_NamedKey_NoModifiers()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[KeyPressAction.KeyKey] = "Enter";

        await new KeyPressAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)8)), default);

        Assert.Equal((ushort)0x0D, senders.SendInput.LastVk);
        Assert.Equal(KeyModifiers.None, senders.SendInput.LastModifiers);
    }

    [Fact]
    public async Task KeyPress_UnknownKey_FailsWithoutSending()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[KeyPressAction.KeyKey] = "NotAKey";

        var result = await new KeyPressAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)9)), default);

        Assert.False(result.Success);
        Assert.Equal(0, senders.SendInput.Calls);
        Assert.Contains("NotAKey", result.ErrorMessage);
    }

    [Fact]
    public async Task KeyPress_BlankKey_FailsWithRequiredMessage()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id }; // no key configured

        var result = await new KeyPressAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)10)), default);

        Assert.False(result.Success);
        Assert.Equal(0, senders.SendInput.Calls);
        Assert.Contains("required", result.ErrorMessage);
    }

    [Theory]
    [InlineData(typeof(TypeTextAction), "input.typeText", "Type Text")]
    [InlineData(typeof(KeyPressAction), "input.keyPress", "Key Press")]
    public void Definition_Metadata(Type actionType, string expectedTypeKey, string expectedDisplayName)
    {
        var def = (IActionDefinition)Activator.CreateInstance(actionType, new Senders().Resolver())!;

        Assert.Equal(expectedTypeKey, def.TypeKey);
        Assert.Equal(expectedDisplayName, def.DisplayName);
        Assert.Equal("Input", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == InputActionBase.MethodKey);
        Assert.False(def.SupportsRetry);
    }

    [Fact]
    public void KeyPress_ConfigFields_KeyAndModifiersThenMethod()
    {
        var def = new KeyPressAction(new Senders().Resolver());

        Assert.Equal(
            new[] { KeyPressAction.KeyKey, KeyPressAction.CtrlKey, KeyPressAction.AltKey, KeyPressAction.ShiftKey, KeyPressAction.WinKey, KeyboardActionBase.KeyDelayKey, InputActionBase.MethodKey },
            def.ConfigFields.Select(f => f.Key));
    }

    [Fact]
    public async Task TypeText_DefaultKeyDelay_Is20()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[TypeTextAction.TextKey] = "hi";

        await new TypeTextAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)5)), default);

        Assert.Equal(KeyboardActionBase.DefaultKeyDelayMs, senders.SendInput.LastKeyDelayMs);
    }

    [Fact]
    public async Task TypeText_KeyDelayOverride_IsPassedToSender()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[TypeTextAction.TextKey] = "hi";
        action.Config[KeyboardActionBase.KeyDelayKey] = 50;

        await new TypeTextAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)5)), default);

        Assert.Equal(50, senders.SendInput.LastKeyDelayMs);
    }

    [Fact]
    public async Task KeyPress_KeyDelayOverride_IsPassedToSender()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var action = new BotAction { TargetId = id };
        action.Config[KeyPressAction.KeyKey] = "Enter";
        action.Config[KeyboardActionBase.KeyDelayKey] = 35;

        await new KeyPressAction(senders.Resolver()).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)8)), default);

        Assert.Equal(35, senders.SendInput.LastKeyDelayMs);
    }
}
