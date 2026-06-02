using System.Text.Json;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class DelayActionTests
{
    private static ActionExecutionContext Ctx(BotAction action)
        => new(action, new BotExecutionContext(), _ => { });

    [Fact]
    public async Task NoDuration_ReturnsOutImmediately()
    {
        var result = await new DelayAction().ExecuteAsync(Ctx(new BotAction()), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
    }

    [Fact]
    public async Task ReadsDurationFromJsonElement_AndReturnsOut()
    {
        var action = new BotAction();
        action.Config[DelayAction.DurationMsKey] = JsonDocument.Parse("1").RootElement;

        var result = await new DelayAction().ExecuteAsync(Ctx(action), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
    }

    [Fact]
    public async Task PositiveDuration_CancelledToken_Throws()
    {
        var action = new BotAction();
        action.Config[DelayAction.DurationMsKey] = 60000;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DelayAction().ExecuteAsync(Ctx(action), cts.Token));
    }

    [Fact]
    public void Definition_HasInOutPorts_AndNoRetry()
    {
        var def = new DelayAction();

        Assert.Equal("control.delay", def.TypeKey);
        Assert.Equal("Control Flow", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.False(def.SupportsRetry);
    }
}
