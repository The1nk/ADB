using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ClickActionTests
{
    private sealed class FakeInputSender : IInputSender
    {
        public int Calls { get; private set; }
        public IntPtr LastWindow { get; private set; }
        public int LastX { get; private set; }
        public int LastY { get; private set; }

        public void Click(IntPtr windowHandle, int clientX, int clientY)
        {
            Calls++;
            LastWindow = windowHandle;
            LastX = clientX;
            LastY = clientY;
        }

        public void RightClick(IntPtr windowHandle, int clientX, int clientY) { }

        public void DoubleClick(IntPtr windowHandle, int clientX, int clientY) { }

        public void MoveTo(IntPtr windowHandle, int clientX, int clientY) { }

        public Task TypeText(IntPtr windowHandle, string text, int keyDelayMs, CancellationToken ct) => Task.CompletedTask;

        public Task KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers, int keyDelayMs, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Senders
    {
        public FakeInputSender SendInput { get; } = new();
        public FakeInputSender PostMessage { get; } = new();
        public InputSenderResolver Resolver() => new(SendInput, PostMessage);
    }

    private static BotAction ClickNode(Guid? targetId, int x = 10, int y = 20, string? method = null)
    {
        var action = new BotAction { TypeKey = "input.click", TargetId = targetId };
        action.Config[ClickAction.XKey] = x;
        action.Config[ClickAction.YKey] = y;
        if (method is not null)
        {
            action.Config[ClickAction.MethodKey] = method;
        }

        return action;
    }

    private static BotExecutionContext WindowContext(Guid targetId, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[targetId] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction action, BotExecutionContext ctx)
        => new(action, ctx, _ => { });

    [Fact]
    public async Task Click_DefaultMethod_UsesSendInput_AtClientCoords_FollowsOnSuccess()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();

        var result = await new ClickAction(senders.Resolver()).ExecuteAsync(Exec(ClickNode(id), WindowContext(id, (IntPtr)4660)), default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal(1, senders.SendInput.Calls);
        Assert.Equal(0, senders.PostMessage.Calls);
        Assert.Equal((IntPtr)4660, senders.SendInput.LastWindow);
        Assert.Equal(10, senders.SendInput.LastX);
        Assert.Equal(20, senders.SendInput.LastY);
    }

    [Fact]
    public async Task Click_PostMessageMethod_UsesPostMessageSender()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();

        var result = await new ClickAction(senders.Resolver()).ExecuteAsync(
            Exec(ClickNode(id, method: InputSenderResolver.PostMessageMethod), WindowContext(id, (IntPtr)5)), default);

        Assert.True(result.Success);
        Assert.Equal(1, senders.PostMessage.Calls);
        Assert.Equal(0, senders.SendInput.Calls);
        Assert.Equal((IntPtr)5, senders.PostMessage.LastWindow);
    }

    [Fact]
    public async Task Click_SendInputMethodExplicit_UsesSendInputSender()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();

        await new ClickAction(senders.Resolver()).ExecuteAsync(
            Exec(ClickNode(id, method: InputSenderResolver.SendInputMethod), WindowContext(id, (IntPtr)7)), default);

        Assert.Equal(1, senders.SendInput.Calls);
        Assert.Equal(0, senders.PostMessage.Calls);
    }

    [Fact]
    public async Task Click_UnknownMethod_FallsBackToSendInput()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();

        await new ClickAction(senders.Resolver()).ExecuteAsync(
            Exec(ClickNode(id, method: "garbage"), WindowContext(id, (IntPtr)3)), default);

        Assert.Equal(1, senders.SendInput.Calls);
        Assert.Equal(0, senders.PostMessage.Calls);
    }

    [Fact]
    public async Task Click_DefaultsToSingleTargetWhenTargetIdNull()
    {
        var senders = new Senders();
        var ctx = new BotExecutionContext();
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = (IntPtr)99 };

        var result = await new ClickAction(senders.Resolver()).ExecuteAsync(Exec(ClickNode(null, x: 1, y: 2), ctx), default);

        Assert.True(result.Success);
        Assert.Equal((IntPtr)99, senders.SendInput.LastWindow);
    }

    [Fact]
    public async Task Click_NoResolvedTarget_FailsWithoutSending()
    {
        var senders = new Senders();

        var result = await new ClickAction(senders.Resolver()).ExecuteAsync(
            Exec(ClickNode(null), new BotExecutionContext()), default);

        Assert.False(result.Success);
        Assert.Equal(0, senders.SendInput.Calls);
        Assert.Equal(0, senders.PostMessage.Calls);
        Assert.Contains("Window target", result.ErrorMessage);
    }

    [Fact]
    public async Task Click_TargetWithoutHandle_Fails()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = null };

        var result = await new ClickAction(senders.Resolver()).ExecuteAsync(Exec(ClickNode(id), ctx), default);

        Assert.False(result.Success);
        Assert.Equal(0, senders.SendInput.Calls);
        Assert.Equal(0, senders.PostMessage.Calls);
    }

    [Fact]
    public async Task Click_ZeroHandle_Fails()
    {
        var id = Guid.NewGuid();
        var senders = new Senders();

        var result = await new ClickAction(senders.Resolver()).ExecuteAsync(Exec(ClickNode(id), WindowContext(id, IntPtr.Zero)), default);

        Assert.False(result.Success);
        Assert.Equal(0, senders.SendInput.Calls);
        Assert.Equal(0, senders.PostMessage.Calls);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new ClickAction(new Senders().Resolver());

        Assert.Equal("input.click", def.TypeKey);
        Assert.Equal("Input", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Equal(new[] { ClickAction.XKey, ClickAction.YKey, ClickAction.MethodKey }, def.ConfigFields.Select(f => f.Key));
        var method = def.ConfigFields.Single(f => f.Key == ClickAction.MethodKey);
        Assert.Equal(new[] { "SendInput", "PostMessage" }, method.Options);
        Assert.False(def.SupportsRetry);
    }
}
