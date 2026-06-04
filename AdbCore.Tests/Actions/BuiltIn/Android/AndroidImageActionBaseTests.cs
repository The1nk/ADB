using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidImageActionBaseTests
{
    private sealed class TestAndroidImageAction(ITemplateMatcher matcher) : AndroidImageActionBase
    {
        private readonly ITemplateMatcher _matcher = matcher;
        public override string TypeKey => "android.test";
        public override string DisplayName => "Test Android Image";
        public override string Description => "";
        protected override IEnumerable<ConfigField> ActionConfigFields => [];
        public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct) => Task.FromResult(ActionResult.Ok(SuccessPort));

        public MatchResult? CallCaptureAndMatch(ActionExecutionContext ctx, IAndroidDevice device, string template, double confidence)
            => CaptureAndMatch(ctx, device, _matcher, template, confidence);
    }

    internal static byte[] PngBytes(int w, int h)
    {
        using var bmp = new Bitmap(w, h);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static ActionExecutionContext Exec(BotAction action) => new(action, new BotExecutionContext(), _ => { });

    [Fact]
    public void CaptureAndMatch_NoRegion_PassesFullFrame_AndReturnsMatchUnchanged()
    {
        var device = new FakeAndroidDevice { ScreenshotBytes = PngBytes(1080, 1920) };
        var matcher = new FakeTemplateMatcher(new MatchResult(50, 60, 10, 8, 0.95));
        var action = new TestAndroidImageAction(matcher);

        var result = action.CallCaptureAndMatch(Exec(new BotAction()), device, "t.png", 0.8);

        Assert.Equal(1080, matcher.LastHaystackWidth);
        Assert.Equal(1920, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(50, 60, 10, 8, 0.95), result);
        Assert.Contains("screenshot", device.Calls);
    }

    [Fact]
    public void CaptureAndMatch_WithRegion_CropsFrame_AndOffsetsResultBack()
    {
        var device = new FakeAndroidDevice { ScreenshotBytes = PngBytes(1080, 1920) };
        var matcher = new FakeTemplateMatcher(new MatchResult(5, 7, 10, 8, 0.9));
        var action = new TestAndroidImageAction(matcher);
        var botAction = new BotAction { Config =
        {
            [TemplateMatchCore.RegionXKey] = 100, [TemplateMatchCore.RegionYKey] = 40,
            [TemplateMatchCore.RegionWidthKey] = 300, [TemplateMatchCore.RegionHeightKey] = 200,
        } };

        var result = action.CallCaptureAndMatch(Exec(botAction), device, "t.png", 0.8);

        Assert.Equal(300, matcher.LastHaystackWidth);
        Assert.Equal(200, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(105, 47, 10, 8, 0.9), result);
    }

    [Fact]
    public void Definition_HasRegionFields_ButNoCaptureMethod_AndSupportsRetry()
    {
        var action = new TestAndroidImageAction(new FakeTemplateMatcher(null));
        Assert.Equal("Android", action.Category);
        Assert.True(action.SupportsRetry);
        Assert.Contains(action.ConfigFields, f => f.Key == TemplateMatchCore.RegionWidthKey);
        Assert.DoesNotContain(action.ConfigFields, f => f.Key == ScreenActionBase.CaptureMethodKey);
    }
}
