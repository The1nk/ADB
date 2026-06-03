using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class FindImageActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static FindImageAction Action(MatchResult? result, int rand = 0)
        => new(new FakeWindowCapture(800, 600), new FakeTemplateMatcher(result), new FixedRandomSource(rand));

    [Fact]
    public async Task Match_WritesAllVariables_AndRoutesSuccess()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { [FindImageAction.TemplatePathKey] = "btn.png" } };

        var result = await Action(new MatchResult(100, 40, 30, 20, 0.97), rand: 123)
            .ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal("100", ctx.Variables["matchLeft"]);
        Assert.Equal("40", ctx.Variables["matchTop"]);
        Assert.Equal("130", ctx.Variables["matchRight"]);
        Assert.Equal("60", ctx.Variables["matchBottom"]);
        Assert.Equal("115", ctx.Variables["matchCenterX"]);
        Assert.Equal("50", ctx.Variables["matchCenterY"]);
        Assert.Equal("123", ctx.Variables["matchRandX"]);
        Assert.Equal("123", ctx.Variables["matchRandY"]);
        Assert.Equal("0.97", ctx.Variables["matchConfidence"]);
    }

    [Fact]
    public async Task RandomPoint_IsWithinRegion()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { [FindImageAction.TemplatePathKey] = "btn.png" } };

        var find = new FindImageAction(new FakeWindowCapture(800, 600), new FakeTemplateMatcher(new MatchResult(100, 40, 30, 20, 0.9)), new SystemRandomSource());
        await find.ExecuteAsync(Exec(action, ctx), default);

        Assert.InRange(int.Parse(ctx.Variables["matchRandX"].ToString()!), 100, 130);
        Assert.InRange(int.Parse(ctx.Variables["matchRandY"].ToString()!), 40, 60);
    }

    [Fact]
    public async Task CustomResultVar_PrefixesVariables()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config =
        {
            [FindImageAction.TemplatePathKey] = "btn.png",
            [FindImageAction.ResultVarKey] = "btn",
        } };

        await Action(new MatchResult(1, 2, 4, 6, 0.9)).ExecuteAsync(Exec(action, ctx), default);

        Assert.Equal("3", ctx.Variables["btnCenterX"]); // 1 + 4/2
        Assert.True(ctx.Variables.ContainsKey("btnConfidence"));
    }

    [Fact]
    public async Task NoMatch_FailsForRetryAndOnFailureRouting_WritesNothing()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { [FindImageAction.TemplatePathKey] = "btn.png" } };

        var result = await Action(null).ExecuteAsync(Exec(action, ctx), default);

        Assert.False(result.Success);
        Assert.Empty(ctx.Variables);
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var action = new BotAction { Config = { [FindImageAction.TemplatePathKey] = "btn.png" } };
        var result = await Action(new MatchResult(0, 0, 1, 1, 1)).ExecuteAsync(Exec(action, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public async Task BlankTemplatePath_Fails()
    {
        var id = Guid.NewGuid();
        var action = new BotAction { TargetId = id };
        var result = await Action(new MatchResult(0, 0, 1, 1, 1)).ExecuteAsync(Exec(action, WindowContext(id, (IntPtr)5)), default);
        Assert.False(result.Success);
        Assert.Contains("template", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = Action(null);
        Assert.Equal("screen.findImage", def.TypeKey);
        Assert.Equal("Find Image", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.True(def.SupportsRetry);
        Assert.Contains(def.ConfigFields, f => f.Key == ScreenActionBase.RegionWidthKey);
        Assert.Contains(def.ConfigFields, f => f.Key == ScreenActionBase.CaptureMethodKey);
    }
}
