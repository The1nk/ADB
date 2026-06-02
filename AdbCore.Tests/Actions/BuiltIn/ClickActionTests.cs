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
    }

    private static (BotAction action, BotExecutionContext ctx, Guid targetId) Setup(IntPtr handle)
    {
        var targetId = Guid.NewGuid();
        var ctx = new BotExecutionContext();
        ctx.Targets[targetId] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        var action = new BotAction { TypeKey = "input.click", TargetId = targetId };
        action.Config[ClickAction.XKey] = 10;
        action.Config[ClickAction.YKey] = 20;
        return (action, ctx, targetId);
    }

    [Fact]
    public async Task Click_SendsToTargetHwndAtClientCoords_AndFollowsOnSuccess()
    {
        var (action, ctx, _) = Setup((IntPtr)4660);
        var sender = new FakeInputSender();

        var result = await new ClickAction(sender).ExecuteAsync(new ActionExecutionContext(action, ctx, _ => { }), default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal(1, sender.Calls);
        Assert.Equal((IntPtr)4660, sender.LastWindow);
        Assert.Equal(10, sender.LastX);
        Assert.Equal(20, sender.LastY);
    }

    [Fact]
    public async Task Click_DefaultsToSingleTargetWhenTargetIdNull()
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = (IntPtr)99 };
        var action = new BotAction { TypeKey = "input.click", TargetId = null };
        action.Config[ClickAction.XKey] = 1;
        action.Config[ClickAction.YKey] = 2;
        var sender = new FakeInputSender();

        var result = await new ClickAction(sender).ExecuteAsync(new ActionExecutionContext(action, ctx, _ => { }), default);

        Assert.True(result.Success);
        Assert.Equal((IntPtr)99, sender.LastWindow);
    }

    [Fact]
    public async Task Click_NoResolvedTarget_FailsWithoutSending()
    {
        var action = new BotAction { TypeKey = "input.click", TargetId = null };
        action.Config[ClickAction.XKey] = 1;
        action.Config[ClickAction.YKey] = 2;
        var sender = new FakeInputSender();

        var result = await new ClickAction(sender).ExecuteAsync(
            new ActionExecutionContext(action, new BotExecutionContext(), _ => { }), default);

        Assert.False(result.Success);
        Assert.Equal(0, sender.Calls);
        Assert.Contains("Window target", result.ErrorMessage);
    }

    [Fact]
    public async Task Click_TargetWithoutHandle_Fails()
    {
        var targetId = Guid.NewGuid();
        var ctx = new BotExecutionContext();
        ctx.Targets[targetId] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = null };
        var action = new BotAction { TypeKey = "input.click", TargetId = targetId };
        var sender = new FakeInputSender();

        var result = await new ClickAction(sender).ExecuteAsync(new ActionExecutionContext(action, ctx, _ => { }), default);

        Assert.False(result.Success);
        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new ClickAction(new FakeInputSender());

        Assert.Equal("input.click", def.TypeKey);
        Assert.Equal("Input", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Equal(new[] { ClickAction.XKey, ClickAction.YKey }, def.ConfigFields.Select(f => f.Key));
        Assert.False(def.SupportsRetry);
    }
}
