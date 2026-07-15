using System;
using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class FrameSourceResolverTests
{
    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    [Fact]
    public void Fresh_InvokesCaptureDelegate_AndSnapshotsIt()
    {
        var ctx = new BotExecutionContext();
        var action = new BotAction();
        var calls = 0;

        var snap = FrameSourceResolver.Acquire(Exec(action, ctx), () => { calls++; return new Bitmap(12, 8, PixelFormat.Format32bppArgb); });

        Assert.Equal(1, calls);
        Assert.Equal(12, snap.Width);
        Assert.Equal(8, snap.Height);
    }

    [Fact]
    public void Stored_ReturnsStoredFrame_WithoutCapturing()
    {
        var ctx = new BotExecutionContext();
        using (var bmp = new Bitmap(30, 20, PixelFormat.Format32bppArgb)) { ctx.Frames.Set("f", FrameSnapshot.FromBitmap(bmp)); }
        var action = new BotAction { Config = { [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue, [FrameSourceConfig.FrameNameKey] = "f" } };
        var calls = 0;

        var snap = FrameSourceResolver.Acquire(Exec(action, ctx), () => { calls++; return new Bitmap(1, 1); });

        Assert.Equal(0, calls);
        Assert.Equal(30, snap.Width);
    }

    [Fact]
    public void Stored_Missing_Throws()
    {
        var ctx = new BotExecutionContext();
        var action = new BotAction { Config = { [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue, [FrameSourceConfig.FrameNameKey] = "nope" } };

        Assert.Throws<InvalidOperationException>(() => FrameSourceResolver.Acquire(Exec(action, ctx), () => new Bitmap(1, 1)));
    }
}
