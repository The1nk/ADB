using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Actions.BuiltIn;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ThrowErrorActionTests
{
    private static ActionExecutionContext Ctx(BotAction action)
        => new(action, new BotExecutionContext(), _ => { });

    [Fact]
    public async Task Execute_ReturnsFailure_WithConfiguredMessage()
    {
        var action = new BotAction();
        action.Config[ThrowErrorAction.MessageKey] = "boom";

        var result = await new ThrowErrorAction().ExecuteAsync(Ctx(action), default);

        Assert.False(result.Success);
        Assert.Equal("boom", result.ErrorMessage);
    }

    [Fact]
    public async Task Execute_MissingMessage_UsesDefault()
    {
        var result = await new ThrowErrorAction().ExecuteAsync(Ctx(new BotAction()), default);

        Assert.False(result.Success);
        Assert.Equal(ThrowErrorAction.DefaultMessage, result.ErrorMessage);
    }

    [Fact]
    public void Definition_IsTerminalControlFlow_NoRetry()
    {
        var def = new ThrowErrorAction();

        Assert.Equal("control.throwError", def.TypeKey);
        Assert.Equal("Control Flow", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Empty(def.OutputPorts);
        Assert.False(def.SupportsRetry);
    }
}
