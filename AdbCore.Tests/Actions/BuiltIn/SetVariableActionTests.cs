using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Tests.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class SetVariableActionTests
{
    private static ActionExecutionContext Ctx(BotAction action, BotExecutionContext context)
        => new(action, context, _ => { });

    [Fact]
    public async Task SetVariable_WritesNameValueIntoContext_AndContinues()
    {
        var action = new BotAction { TypeKey = "data.setVariable" };
        action.Config[SetVariableAction.NameKey] = "greeting";
        action.Config[SetVariableAction.ValueKey] = "hello";
        var context = new BotExecutionContext();

        var result = await new SetVariableAction().ExecuteAsync(Ctx(action, context), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Equal("hello", context.Variables["greeting"]);
    }

    [Fact]
    public async Task SetVariable_OverwritesExistingValue()
    {
        var action = new BotAction { TypeKey = "data.setVariable" };
        action.Config[SetVariableAction.NameKey] = "x";
        action.Config[SetVariableAction.ValueKey] = "2";
        var context = new BotExecutionContext();
        context.Variables["x"] = "1";

        await new SetVariableAction().ExecuteAsync(Ctx(action, context), default);

        Assert.Equal("2", context.Variables["x"]);
    }

    [Fact]
    public async Task SetVariable_EmptyName_IsNoOp_AndContinues()
    {
        var action = new BotAction { TypeKey = "data.setVariable" };
        action.Config[SetVariableAction.ValueKey] = "orphan";
        var context = new BotExecutionContext();

        var result = await new SetVariableAction().ExecuteAsync(Ctx(action, context), default);

        Assert.True(result.Success);
        Assert.Empty(context.Variables);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new SetVariableAction();

        Assert.Equal("data.setVariable", def.TypeKey);
        Assert.Equal("Data", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Equal(new[] { SetVariableAction.NameKey, SetVariableAction.ValueKey }, def.ConfigFields.Select(f => f.Key));
        Assert.False(def.SupportsRetry);
    }

    [Fact]
    public async Task SetVariable_FeedsBranchCondition_ThroughEngine()
    {
        var setVar = new BotAction { Id = Guid.NewGuid(), TypeKey = "data.setVariable" };
        setVar.Config[SetVariableAction.NameKey] = "x";
        setVar.Config[SetVariableAction.ValueKey] = "5";

        var branch = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.branch" };
        branch.Config[BranchAction.VariableKey] = "x";
        branch.Config[BranchAction.OperatorKey] = BranchAction.OpGreaterThan;
        branch.Config[BranchAction.ValueKey] = "3";

        var yes = new BotAction { Id = Guid.NewGuid(), TypeKey = "yes" };
        var no = new BotAction { Id = Guid.NewGuid(), TypeKey = "no" };

        var bot = new Bot { Name = "setvar-branch" };
        bot.Actions.AddRange(new[] { setVar, branch, yes, no });
        bot.Connections.Add(new ActionConnection { Id = Guid.NewGuid(), SourceActionId = setVar.Id, SourcePort = "out", TargetActionId = branch.Id, TargetPort = "in" });
        bot.Connections.Add(new ActionConnection { Id = Guid.NewGuid(), SourceActionId = branch.Id, SourcePort = BranchAction.TruePort, TargetActionId = yes.Id, TargetPort = "in" });
        bot.Connections.Add(new ActionConnection { Id = Guid.NewGuid(), SourceActionId = branch.Id, SourcePort = BranchAction.FalsePort, TargetActionId = no.Id, TargetPort = "in" });

        var yesRan = false;
        var noRan = false;
        var registry = new ActionExecutorRegistry();
        registry.Register(new SetVariableAction());
        registry.Register(new BranchAction());
        registry.Register(new FakeExecutor { TypeKey = "yes", Behavior = c => { yesRan = true; return ActionResult.Ok(string.Empty); } });
        registry.Register(new FakeExecutor { TypeKey = "no", Behavior = c => { noRan = true; return ActionResult.Ok(string.Empty); } });

        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), null, default);

        Assert.True(result.Success);
        Assert.True(yesRan);
        Assert.False(noRan);
    }
}
