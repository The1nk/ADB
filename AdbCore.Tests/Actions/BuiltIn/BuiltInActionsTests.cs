using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class BuiltInActionsTests
{
    private static ActionExecutionContext Ctx(BotAction action, Action<string> log)
        => new(action, new BotExecutionContext(), log);

    [Fact]
    public void Register_AddsAllBuiltInsToBothRegistries()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();

        BuiltInActions.Register(defs, execs);

        foreach (var key in new[]
        {
            "control.start", "control.end", "data.log", "control.delay", "control.branch",
            "data.setVariable", "data.comment",
            "input.click", "input.rightClick", "input.doubleClick", "input.mouseMove",
            "input.typeText", "input.keyPress",
        })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.True(execs.TryGet(key, out _));
        }

        // Engine-native nodes: definitions only, no executors.
        foreach (var key in new[] { "control.loop", "control.runParallel", "control.join" })
        {
            Assert.True(defs.TryGet(key, out _));
            Assert.False(execs.TryGet(key, out _));
        }

        Assert.Equal(44, defs.Count);
        Assert.Equal(41, execs.Count);
    }

    [Fact]
    public async Task Start_ReturnsOutPort()
    {
        var result = await new StartAction().ExecuteAsync(Ctx(new BotAction(), _ => { }), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
    }

    [Fact]
    public async Task End_IsTerminal()
    {
        var result = await new EndAction().ExecuteAsync(Ctx(new BotAction(), _ => { }), default);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.OutputPort);
    }

    [Fact]
    public async Task Log_EmitsConfiguredMessage_AndContinues()
    {
        var action = new BotAction { TypeKey = "data.log" };
        action.Config[LogAction.MessageKey] = "hello world";
        var captured = new List<string>();

        var result = await new LogAction().ExecuteAsync(Ctx(action, captured.Add), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Equal(new[] { "hello world" }, captured);
    }

    [Fact]
    public async Task Log_MissingMessage_EmitsEmptyString()
    {
        var captured = new List<string>();

        await new LogAction().ExecuteAsync(Ctx(new BotAction { TypeKey = "data.log" }, captured.Add), default);

        Assert.Equal(new[] { string.Empty }, captured);
    }
}
