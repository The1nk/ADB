using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;
using AdbCore.Tests.Input;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class MouseActionsTests
{
    private sealed class Senders
    {
        public RecordingInputSender SendInput { get; } = new();
        public RecordingInputSender PostMessage { get; } = new();
        public InputSenderResolver Resolver() => new(SendInput, PostMessage);
    }

    private static (BotAction action, BotExecutionContext ctx) Setup(IntPtr handle, int x = 30, int y = 40, string? method = null)
    {
        var id = Guid.NewGuid();
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        var action = new BotAction { TargetId = id };
        action.Config[PointerActionBase.XKey] = x;
        action.Config[PointerActionBase.YKey] = y;
        if (method is not null)
        {
            action.Config[PointerActionBase.MethodKey] = method;
        }

        return (action, ctx);
    }

    private static ActionExecutionContext Exec(BotAction action, BotExecutionContext ctx) => new(action, ctx, _ => { });

    [Fact]
    public async Task RightClick_DispatchesRightClick_ViaSendInput_Default()
    {
        var senders = new Senders();
        var (action, ctx) = Setup((IntPtr)11);

        var result = await new RightClickAction(senders.Resolver()).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal("RightClick", senders.SendInput.LastOp);
        Assert.Equal((IntPtr)11, senders.SendInput.LastWindow);
        Assert.Equal(30, senders.SendInput.LastX);
        Assert.Equal(40, senders.SendInput.LastY);
        Assert.Equal(0, senders.PostMessage.Calls);
    }

    [Fact]
    public async Task DoubleClick_DispatchesDoubleClick()
    {
        var senders = new Senders();
        var (action, ctx) = Setup((IntPtr)12);

        await new DoubleClickAction(senders.Resolver()).ExecuteAsync(Exec(action, ctx), default);

        Assert.Equal("DoubleClick", senders.SendInput.LastOp);
        Assert.Equal((IntPtr)12, senders.SendInput.LastWindow);
    }

    [Fact]
    public async Task MouseMove_DispatchesMoveTo()
    {
        var senders = new Senders();
        var (action, ctx) = Setup((IntPtr)13, x: 5, y: 6);

        await new MouseMoveAction(senders.Resolver()).ExecuteAsync(Exec(action, ctx), default);

        Assert.Equal("MoveTo", senders.SendInput.LastOp);
        Assert.Equal(5, senders.SendInput.LastX);
        Assert.Equal(6, senders.SendInput.LastY);
    }

    [Fact]
    public async Task PointerAction_PostMessageMethod_RoutesToPostMessageSender()
    {
        var senders = new Senders();
        var (action, ctx) = Setup((IntPtr)14, method: InputSenderResolver.PostMessageMethod);

        await new RightClickAction(senders.Resolver()).ExecuteAsync(Exec(action, ctx), default);

        Assert.Equal("RightClick", senders.PostMessage.LastOp);
        Assert.Equal(0, senders.SendInput.Calls);
    }

    [Fact]
    public async Task PointerAction_NoResolvedTarget_FailsWithoutSending()
    {
        var senders = new Senders();
        var action = new BotAction { TargetId = null };

        var result = await new MouseMoveAction(senders.Resolver()).ExecuteAsync(
            Exec(action, new BotExecutionContext()), default);

        Assert.False(result.Success);
        Assert.Equal(0, senders.SendInput.Calls);
        Assert.Equal(0, senders.PostMessage.Calls);
        Assert.Contains("Window target", result.ErrorMessage);
    }

    [Theory]
    [InlineData(typeof(RightClickAction), "input.rightClick", "Right Click")]
    [InlineData(typeof(DoubleClickAction), "input.doubleClick", "Double Click")]
    [InlineData(typeof(MouseMoveAction), "input.mouseMove", "Mouse Move")]
    public void Definition_Metadata(Type actionType, string expectedTypeKey, string expectedDisplayName)
    {
        var def = (IActionDefinition)Activator.CreateInstance(actionType, new Senders().Resolver())!;

        Assert.Equal(expectedTypeKey, def.TypeKey);
        Assert.Equal(expectedDisplayName, def.DisplayName);
        Assert.Equal("Input", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Equal(new[] { PointerActionBase.XKey, PointerActionBase.YKey, PointerActionBase.MethodKey }, def.ConfigFields.Select(f => f.Key));
        Assert.False(def.SupportsRetry);
    }
}
