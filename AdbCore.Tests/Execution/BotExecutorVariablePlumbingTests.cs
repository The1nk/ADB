using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class BotExecutorVariablePlumbingTests
{
    private static Bot LinearBot(out Guid setId)
    {
        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var set = new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "data.setVariable",
            Config = { ["name"] = "greeting", ["value"] = "hi" },
        };
        setId = set.Id;
        var bot = new Bot { Id = Guid.NewGuid(), Name = "Lin", Actions = { start, set } };
        bot.Connections.Add(new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = set.Id, TargetPort = "in" });
        return bot;
    }

    private static (ActionRegistry, ActionExecutorRegistry) Registries()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);
        return (defs, execs);
    }

    [Fact]
    public async Task FinalVariables_CapturesVariablesSetDuringRun()
    {
        var bot = LinearBot(out _);
        var (_, execs) = Registries();
        var result = await new BotExecutor(execs).RunAsync(bot, new ExecutionOptions(), null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.FinalVariables.ContainsKey("greeting"));
        Assert.Equal("hi", result.FinalVariables["greeting"]);
    }

    [Fact]
    public async Task InitialVariables_SeedTheRun()
    {
        var bot = LinearBot(out _);
        var (_, execs) = Registries();
        var options = new ExecutionOptions { InitialVariables = new Dictionary<string, object> { ["seed"] = 42 } };
        var result = await new BotExecutor(execs).RunAsync(bot, options, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(42, result.FinalVariables["seed"]);
    }
}
