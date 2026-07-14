using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidStoredFrameSourceTests
{
    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static (BotExecutionContext ctx, FakeAndroidDevice dev) DeviceContext(Guid id)
    {
        var dev = new FakeAndroidDevice();
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:x", Handle = dev };
        return (ctx, dev);
    }

    private static byte[] PngBytes(int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public async Task StoredSource_MatchesStoredFrame_WithoutScreenshot()
    {
        var id = Guid.NewGuid();
        var (ctx, dev) = DeviceContext(id);
        using (var bmp = new Bitmap(64, 48, PixelFormat.Format32bppArgb))
        {
            ctx.Frames.Set("f", FrameSnapshot.FromBitmap(bmp));
        }

        var matcher = new FakeTemplateMatcher(new MatchResult(1, 2, 3, 4, 0.9));
        var action = new BotAction { TargetId = id, Config =
        {
            [TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(new byte[] { 1 }),
            [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue,
            [FrameSourceConfig.FrameNameKey] = "f",
        } };

        var result = await new AndroidFindImageAction(matcher, new FixedRandomSource(0)).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.DoesNotContain("screenshot", dev.Calls); // device screenshot bypassed
        Assert.Equal(64, matcher.LastHaystackWidth);
    }

    [Fact]
    public async Task FreshSource_StillTakesScreenshot_Default()
    {
        var id = Guid.NewGuid();
        var (ctx, dev) = DeviceContext(id);
        dev.ScreenshotBytes = PngBytes(64, 48);
        var matcher = new FakeTemplateMatcher(new MatchResult(1, 2, 3, 4, 0.9));
        var action = new BotAction { TargetId = id, Config = { [TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(new byte[] { 1 }) } };

        await new AndroidFindImageAction(matcher, new FixedRandomSource(0)).ExecuteAsync(Exec(action, ctx), default);

        Assert.Contains("screenshot", dev.Calls);
        Assert.Equal(64, matcher.LastHaystackWidth);
    }

    [Fact]
    public async Task StoredSource_MissingFrame_Throws()
    {
        var id = Guid.NewGuid();
        var (ctx, _) = DeviceContext(id);
        var action = new BotAction { TargetId = id, Config =
        {
            [TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(new byte[] { 1 }),
            [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue,
            [FrameSourceConfig.FrameNameKey] = "missing",
        } };
        var find = new AndroidFindImageAction(new FakeTemplateMatcher(new MatchResult(0, 0, 1, 1, 1)), new FixedRandomSource(0));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await find.ExecuteAsync(Exec(action, ctx), default));
    }

    [Fact]
    public void AndroidFindImage_Definition_IncludesSourceFields()
    {
        var def = new AndroidFindImageAction(new FakeTemplateMatcher(null), new FixedRandomSource(0));
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.FrameNameKey);
    }

    [Fact]
    public void AndroidWaitForImage_Definition_IncludesSourceFields()
    {
        var def = new AndroidWaitForImageAction(new FakeTemplateMatcher(null), new FixedRandomSource(0));
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.FrameNameKey);
    }

    [Fact]
    public void AndroidAssertImageAbsent_Definition_IncludesSourceFields()
    {
        var def = new AndroidAssertImageAbsentAction(new FakeTemplateMatcher(null));
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.FrameNameKey);
    }
}
