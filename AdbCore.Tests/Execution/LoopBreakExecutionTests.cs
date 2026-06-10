using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class LoopBreakExecutionTests
{
    private static BotAction Node(string typeKey, out Guid id)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    [Fact]
    public async Task LoopBreak_ExitsCountLoopEarly_ThenFollowsDone()
    {
        // Count=5 loop; body routes to Loop-Break once the index reaches 2 (its 3rd iteration).
        var loop = Node(LoopAction.LoopTypeKey, out var loopId);
        loop.Config[LoopAction.ModeKey] = LoopAction.ModeCount;
        loop.Config[LoopAction.CountKey] = 5;
        loop.Config[LoopAction.IndexVariableKey] = "i";
        var body = Node("body", out var bodyId);
        var brk = Node(LoopBreakAction.LoopBreakTypeKey, out var brkId);
        var done = Node("done", out var doneId);

        var bot = new Bot { Name = "loopbreak-count" };
        bot.Actions.AddRange(new[] { loop, body, brk, done });
        bot.Connections.Add(Edge(loopId, LoopAction.BodyPort, bodyId));
        bot.Connections.Add(Edge(bodyId, "brk", brkId));       // body -> Loop-Break (taken at i>=2)
        bot.Connections.Add(Edge(loopId, LoopAction.DonePort, doneId));

        var bodyCalls = 0;
        var doneReached = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new FakeExecutor
        {
            TypeKey = "body",
            Behavior = c =>
            {
                bodyCalls++;
                var i = ConfigValues.GetIntVar(c.Context.Variables, "i");
                return ActionResult.Ok(i >= 2 ? "brk" : "out"); // "out" is unwired -> iteration ends, loop continues
            },
        });
        registry.Register(new FakeExecutor { TypeKey = "done", Behavior = c => { doneReached = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.Equal(3, bodyCalls);   // i=0 (out), i=1 (out), i=2 (brk) -> break; iterations 4 & 5 skipped
        Assert.True(doneReached);
    }
}
