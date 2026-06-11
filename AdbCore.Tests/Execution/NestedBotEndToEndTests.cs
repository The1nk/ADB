using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class NestedBotEndToEndTests
{
    [Fact]
    public async Task ParentRunsNestedCard_AndReceivesVar()
    {
        // Child: Start -> SetVariable(result=ok) -> (end of path)
        var cStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var cSet = new BotAction { Id = Guid.NewGuid(), TypeKey = "data.setVariable", Config = { ["name"] = "result", ["value"] = "ok" } };
        var child = new Bot { Id = Guid.NewGuid(), Name = "Child", Actions = { cStart, cSet } };
        child.Connections.Add(new ActionConnection { SourceActionId = cStart.Id, SourcePort = "out", TargetActionId = cSet.Id, TargetPort = "in" });

        // Parent: Start -> NestedBot(receiveVars) -> (end)
        var pStart = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var card = new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "control.nestedBot",
            Config = { ["nestedBotId"] = child.Id.ToString(), ["receiveVars"] = true },
        };
        var parent = new Bot { Id = Guid.NewGuid(), Name = "Parent", NestedBots = { child }, Actions = { pStart, card } };
        parent.Connections.Add(new ActionConnection { SourceActionId = pStart.Id, SourcePort = "out", TargetActionId = card.Id, TargetPort = "in" });

        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        var result = await new BotExecutor(execs).RunAsync(parent, new ExecutionOptions(), null, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("ok", result.FinalVariables["result"]);
    }
}
