using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class CommentActionTests
{
    private static ActionExecutionContext Ctx(BotAction action)
        => new(action, new BotExecutionContext(), _ => { });

    [Fact]
    public async Task Comment_IsNoOp_AndContinues()
    {
        var action = new BotAction { TypeKey = "data.comment" };
        action.Config[CommentAction.TextKey] = "remember to tune confidence";

        var result = await new CommentAction().ExecuteAsync(Ctx(action), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
    }

    [Fact]
    public async Task Comment_NoText_StillContinues()
    {
        var result = await new CommentAction().ExecuteAsync(Ctx(new BotAction { TypeKey = "data.comment" }), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new CommentAction();

        Assert.Equal("data.comment", def.TypeKey);
        Assert.Equal("Data", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        var text = def.ConfigFields.Single(f => f.Key == CommentAction.TextKey);
        Assert.Equal(ConfigFieldType.MultilineString, text.Type);
        Assert.False(def.SupportsRetry);
    }
}
