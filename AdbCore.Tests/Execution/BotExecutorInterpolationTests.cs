using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class BotExecutorInterpolationTests
{
    [Fact]
    public async Task LeafConfig_IsInterpolated_FromRunVariables()
    {
        var execs = new ActionExecutorRegistry();
        execs.Register(new StartAction());
        execs.Register(new EndAction());
        execs.Register(new SetVariableAction());
        execs.Register(new LogAction());

        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var setFoo = new BotAction { Id = Guid.NewGuid(), TypeKey = "data.setVariable", Config = { [SetVariableAction.NameKey] = "foo", [SetVariableAction.ValueKey] = "bar" } };
        var log = new BotAction { Id = Guid.NewGuid(), TypeKey = "data.log", Config = { [LogAction.MessageKey] = "${foo}" } };
        var end = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.end" };

        var bot = new Bot { Actions = { start, setFoo, log, end }, Connections =
        {
            new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = setFoo.Id, TargetPort = "in" },
            new ActionConnection { SourceActionId = setFoo.Id, SourcePort = "out", TargetActionId = log.Id, TargetPort = "in" },
            new ActionConnection { SourceActionId = log.Id, SourcePort = "out", TargetActionId = end.Id, TargetPort = "in" },
        } };

        var logs = new List<string>();
        var result = await new BotExecutor(execs).RunAsync(bot, new ExecutionOptions { Log = logs.Add }, null, default);

        Assert.True(result.Success);
        Assert.Contains("bar", logs);   // ${foo} resolved before the Log action ran
    }
}
