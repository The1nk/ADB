using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using AdbCore.Tests.Targets;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class StoredFrameSourceTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = new FakeWindowHandle(handle) };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    [Fact]
    public async Task StoredSource_MatchesStoredFrame_WithoutFreshCapture()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        using (var bmp = new Bitmap(50, 40, PixelFormat.Format32bppArgb))
        {
            ctx.Frames.Set("f", FrameSnapshot.FromBitmap(bmp));
        }

        var capture = new FakeWindowCapture(800, 600);
        var matcher = new FakeTemplateMatcher(new MatchResult(1, 2, 3, 4, 0.9));
        var action = new BotAction { TargetId = id, Config =
        {
            [TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(new byte[] { 1 }),
            [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue,
            [FrameSourceConfig.FrameNameKey] = "f",
        } };

        var result = await new FindImageAction(capture, matcher, new FixedRandomSource(0)).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal(0, capture.Calls);              // fresh capture bypassed
        Assert.Equal(50, matcher.LastHaystackWidth); // matched against the stored 50x40 frame
        Assert.Equal(40, matcher.LastHaystackHeight);
    }

    [Fact]
    public async Task FreshSource_StillCaptures_Default()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var capture = new FakeWindowCapture(800, 600);
        var matcher = new FakeTemplateMatcher(new MatchResult(1, 2, 3, 4, 0.9));
        var action = new BotAction { TargetId = id, Config =
        {
            [TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(new byte[] { 1 }),
        } };

        await new FindImageAction(capture, matcher, new FixedRandomSource(0)).ExecuteAsync(Exec(action, ctx), default);

        Assert.Equal(1, capture.Calls);
        Assert.Equal(800, matcher.LastHaystackWidth);
    }

    [Fact]
    public async Task StoredSource_MissingFrame_Throws()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config =
        {
            [TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(new byte[] { 1 }),
            [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue,
            [FrameSourceConfig.FrameNameKey] = "missing",
        } };

        var find = new FindImageAction(new FakeWindowCapture(800, 600), new FakeTemplateMatcher(new MatchResult(0, 0, 1, 1, 1)), new FixedRandomSource(0));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await find.ExecuteAsync(Exec(action, ctx), default));
    }

    [Fact]
    public void FindImage_Definition_IncludesSourceFields()
    {
        var def = new FindImageAction(new FakeWindowCapture(8, 8), new FakeTemplateMatcher(null), new FixedRandomSource(0));
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.FrameNameKey);
    }
}
