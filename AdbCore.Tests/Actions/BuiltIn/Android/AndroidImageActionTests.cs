using System.Collections.Generic;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidImageActionTests
{
    private static (ActionExecutionContext ctx, FakeAndroidDevice dev) WithDevice(BotAction action, int w = 1080, int h = 1920)
    {
        var dev = new FakeAndroidDevice { ScreenshotBytes = AndroidImageActionBaseTests.PngBytes(w, h) };
        var ctx = new BotExecutionContext();
        var id = action.TargetId ?? Guid.NewGuid();
        action.TargetId = id;
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:x", Handle = dev };
        return (new ActionExecutionContext(action, ctx, _ => { }), dev);
    }

    private static AndroidFindImageAction Find(MatchResult? result, int rand = 0)
        => new(new FakeTemplateMatcher(result), new FixedRandomSource(rand));

    [Fact]
    public async Task Find_Match_WritesAllVariables_AndRoutesSuccess()
    {
        var action = new BotAction { Config = { [TemplateMatchCore.TemplatePathKey] = "btn.png" } };
        var (ctx, _) = WithDevice(action);

        var result = await Find(new MatchResult(100, 40, 30, 20, 0.97), rand: 123).ExecuteAsync(ctx, default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.Equal("100", ctx.Context.Variables["matchLeft"]);
        Assert.Equal("130", ctx.Context.Variables["matchRight"]);
        Assert.Equal("115", ctx.Context.Variables["matchCenterX"]);
        Assert.Equal("123", ctx.Context.Variables["matchRandX"]);
        Assert.Equal("0.97", ctx.Context.Variables["matchConfidence"]);
    }

    [Fact]
    public async Task Find_CustomResultVar_PrefixesVariables()
    {
        var action = new BotAction { Config =
        {
            [TemplateMatchCore.TemplatePathKey] = "btn.png",
            [TemplateMatchCore.ResultVarKey] = "btn",
        } };
        var (ctx, _) = WithDevice(action);

        await Find(new MatchResult(1, 2, 4, 6, 0.9)).ExecuteAsync(ctx, default);

        Assert.Equal("3", ctx.Context.Variables["btnCenterX"]); // 1 + 4/2
        Assert.True(ctx.Context.Variables.ContainsKey("btnConfidence"));
    }

    [Fact]
    public async Task Find_NoMatch_Fails_WritesNothing()
    {
        var action = new BotAction { Config = { [TemplateMatchCore.TemplatePathKey] = "btn.png" } };
        var (ctx, _) = WithDevice(action);

        var result = await Find(null).ExecuteAsync(ctx, default);

        Assert.False(result.Success);
        Assert.Empty(ctx.Context.Variables);
    }

    [Fact]
    public async Task Find_NoDevice_Fails()
    {
        var exec = new ActionExecutionContext(new BotAction { Config = { [TemplateMatchCore.TemplatePathKey] = "btn.png" } }, new BotExecutionContext(), _ => { });
        var result = await Find(new MatchResult(0, 0, 1, 1, 1)).ExecuteAsync(exec, default);
        Assert.False(result.Success);
        Assert.Contains("Android", result.ErrorMessage);
    }

    [Fact]
    public async Task Find_BlankTemplatePath_Fails()
    {
        var action = new BotAction();
        var (ctx, _) = WithDevice(action);
        var result = await Find(new MatchResult(0, 0, 1, 1, 1)).ExecuteAsync(ctx, default);
        Assert.False(result.Success);
        Assert.Contains("template", result.ErrorMessage);
    }

    [Fact]
    public void Find_Definition_Metadata()
    {
        var def = Find(null);
        Assert.Equal("android.findImage", def.TypeKey);
        Assert.Equal("Find Image (Android)", def.DisplayName);
        Assert.Equal("Android", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.True(def.SupportsRetry);
        Assert.Contains(def.ConfigFields, f => f.Key == TemplateMatchCore.RegionWidthKey);
        Assert.DoesNotContain(def.ConfigFields, f => f.Key == ScreenActionBase.CaptureMethodKey);
    }
}
