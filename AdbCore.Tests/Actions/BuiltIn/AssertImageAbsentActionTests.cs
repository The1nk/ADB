using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class AssertImageAbsentActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static AssertImageAbsentAction Action(MatchResult? result)
        => new(new FakeWindowCapture(800, 600), new FakeTemplateMatcher(result));

    private static BotAction Cfg(Guid id) => new() { TargetId = id, Config = { [ScreenActionBase.TemplatePathKey] = "t.png" } };

    [Fact]
    public async Task ImageAbsent_Succeeds()
    {
        var id = Guid.NewGuid();
        var result = await Action(null).ExecuteAsync(Exec(Cfg(id), WindowContext(id, (IntPtr)5)), default);
        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
    }

    [Fact]
    public async Task ImagePresent_Fails()
    {
        var id = Guid.NewGuid();
        var result = await Action(new MatchResult(1, 2, 3, 4, 0.99)).ExecuteAsync(Exec(Cfg(id), WindowContext(id, (IntPtr)5)), default);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var result = await Action(null).ExecuteAsync(Exec(new BotAction { Config = { [ScreenActionBase.TemplatePathKey] = "t.png" } }, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public async Task BlankTemplate_Fails()
    {
        var id = Guid.NewGuid();
        var result = await Action(null).ExecuteAsync(Exec(new BotAction { TargetId = id }, WindowContext(id, (IntPtr)5)), default);
        Assert.False(result.Success);
        Assert.Contains("template", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = Action(null);
        Assert.Equal("screen.assertImageAbsent", def.TypeKey);
        Assert.Equal("Assert Image Absent", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.True(def.SupportsRetry);
        Assert.DoesNotContain(def.ConfigFields, f => f.Key == ScreenActionBase.ResultVarKey);
    }
}
