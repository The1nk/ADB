using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class WaitForImageActionTests
{
    private sealed class FlakyMatcher(int missesBeforeHit, MatchResult hit) : ITemplateMatcher
    {
        public int Calls { get; private set; }
        public MatchResult? Match(System.Drawing.Bitmap haystack, string templatePath, double minConfidence)
            => ++Calls > missesBeforeHit ? hit : null;
    }

    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static BotAction WaitAction(Guid id, int timeoutMs, int pollMs)
        => new() { TargetId = id, Config =
        {
            [ScreenActionBase.TemplatePathKey] = "t.png",
            [WaitForImageAction.TimeoutMsKey] = timeoutMs,
            [WaitForImageAction.PollIntervalMsKey] = pollMs,
        } };

    [Fact]
    public async Task AppearsAfterPolls_WritesVariables_AndSucceeds()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var matcher = new FlakyMatcher(2, new MatchResult(100, 40, 30, 20, 0.95));
        var action = new WaitForImageAction(new FakeWindowCapture(800, 600), matcher, new FixedRandomSource(7));

        var result = await action.ExecuteAsync(Exec(WaitAction(id, timeoutMs: 5000, pollMs: 1), ctx), default);

        Assert.True(result.Success);
        Assert.Equal("onSuccess", result.OutputPort);
        Assert.True(matcher.Calls >= 3);
        Assert.Equal("115", ctx.Variables["matchCenterX"]);
        Assert.Equal("7", ctx.Variables["matchRandX"]);
    }

    [Fact]
    public async Task NeverAppears_TimesOut_Fails_AndWritesNothing()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new WaitForImageAction(new FakeWindowCapture(800, 600), new FakeTemplateMatcher(null), new FixedRandomSource(0));

        var result = await action.ExecuteAsync(Exec(WaitAction(id, timeoutMs: 0, pollMs: 1), ctx), default);

        Assert.False(result.Success);
        Assert.Empty(ctx.Variables);
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var action = new WaitForImageAction(new FakeWindowCapture(10, 10), new FakeTemplateMatcher(null), new FixedRandomSource(0));
        var a = new BotAction { Config = { [ScreenActionBase.TemplatePathKey] = "t.png" } };
        var result = await action.ExecuteAsync(Exec(a, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new WaitForImageAction(new FakeWindowCapture(1, 1), new FakeTemplateMatcher(null), new FixedRandomSource(0));
        Assert.Equal("screen.waitForImage", def.TypeKey);
        Assert.Equal("Wait for Image", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == WaitForImageAction.TimeoutMsKey);
        Assert.Contains(def.ConfigFields, f => f.Key == ScreenActionBase.RegionWidthKey);
    }
}
