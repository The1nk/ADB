using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ScreenActionBaseTests
{
    private sealed class TestScreenAction(IWindowCapture capture, ITemplateMatcher matcher) : ScreenActionBase(capture)
    {
        private readonly ITemplateMatcher _matcher = matcher;

        public override string TypeKey => "screen.test";
        public override string DisplayName => "Test Screen";
        public override string Description => "";
        public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = SuccessPort, Label = "On Success" } };
        protected override IEnumerable<ConfigField> ActionConfigFields => [];
        public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct) => Task.FromResult(ActionResult.Ok(SuccessPort));

        public MatchResult? CallCaptureAndMatch(ActionExecutionContext ctx, IntPtr hwnd, string template, double confidence)
            => CaptureAndMatch(ctx, hwnd, _matcher, template, confidence);
    }

    private static ActionExecutionContext Exec(BotAction action) => new(action, new BotExecutionContext(), _ => { });

    [Fact]
    public void CaptureAndMatch_NoRegion_PassesFullHaystack_AndReturnsMatchUnchanged()
    {
        var capture = new FakeWindowCapture(1920, 1080);
        var matcher = new FakeTemplateMatcher(new MatchResult(50, 60, 10, 8, 0.95));
        var action = new TestScreenAction(capture, matcher);

        var result = action.CallCaptureAndMatch(Exec(new BotAction()), (IntPtr)1, "t.png", 0.8);

        Assert.Equal(1920, matcher.LastHaystackWidth);
        Assert.Equal(1080, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(50, 60, 10, 8, 0.95), result);
    }

    [Fact]
    public void CaptureAndMatch_WithRegion_CropsHaystack_AndOffsetsResultBack()
    {
        var capture = new FakeWindowCapture(1920, 1080);
        var matcher = new FakeTemplateMatcher(new MatchResult(5, 7, 10, 8, 0.9)); // crop-local coords
        var action = new TestScreenAction(capture, matcher);
        var botAction = new BotAction { Config =
        {
            [ScreenActionBase.RegionXKey] = 100, [ScreenActionBase.RegionYKey] = 40,
            [ScreenActionBase.RegionWidthKey] = 300, [ScreenActionBase.RegionHeightKey] = 200,
        } };

        var result = action.CallCaptureAndMatch(Exec(botAction), (IntPtr)1, "t.png", 0.8);

        Assert.Equal(300, matcher.LastHaystackWidth);   // matcher saw the crop
        Assert.Equal(200, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(105, 47, 10, 8, 0.9), result); // 5+100, 7+40
    }

    [Fact]
    public void CaptureAndMatch_RegionClampedToWindow()
    {
        var capture = new FakeWindowCapture(200, 150);
        var matcher = new FakeTemplateMatcher(new MatchResult(0, 0, 1, 1, 0.9));
        var action = new TestScreenAction(capture, matcher);
        var botAction = new BotAction { Config =
        {
            [ScreenActionBase.RegionXKey] = 180, [ScreenActionBase.RegionYKey] = 140,
            [ScreenActionBase.RegionWidthKey] = 999, [ScreenActionBase.RegionHeightKey] = 999,
        } };

        action.CallCaptureAndMatch(Exec(botAction), (IntPtr)1, "t.png", 0.8);

        Assert.Equal(20, matcher.LastHaystackWidth);   // 200-180
        Assert.Equal(10, matcher.LastHaystackHeight);  // 150-140
    }

    [Fact]
    public void CaptureMethod_DefaultsAuto_AndHonorsBitBltOverride()
    {
        var capture = new FakeWindowCapture(10, 10);
        var action = new TestScreenAction(capture, new FakeTemplateMatcher(null));

        action.CallCaptureAndMatch(Exec(new BotAction()), (IntPtr)1, "t.png", 0.8);
        Assert.Equal(ScreenCaptureMethod.Auto, capture.LastMethod);

        var bitblt = new BotAction { Config = { [ScreenActionBase.CaptureMethodKey] = nameof(ScreenCaptureMethod.BitBlt) } };
        action.CallCaptureAndMatch(Exec(bitblt), (IntPtr)1, "t.png", 0.8);
        Assert.Equal(ScreenCaptureMethod.BitBlt, capture.LastMethod);
    }
}
