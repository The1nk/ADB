using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Execution;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Execution;

public class FrameStoreTests
{
    private static FrameSnapshot Snap(int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        return FrameSnapshot.FromBitmap(bmp);
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsFrame()
    {
        var store = new FrameStore();
        store.Set("hp", Snap(20, 10));

        Assert.True(store.TryGet("hp", out var snap));
        Assert.Equal(20, snap!.Width);
    }

    [Fact]
    public void TryGet_Missing_ReturnsFalse()
    {
        var store = new FrameStore();
        Assert.False(store.TryGet("nope", out var snap));
        Assert.Null(snap);
    }

    [Fact]
    public void Set_Overwrites_SameName()
    {
        var store = new FrameStore();
        store.Set("f", Snap(10, 10));
        store.Set("f", Snap(30, 30));

        Assert.True(store.TryGet("f", out var snap));
        Assert.Equal(30, snap!.Width);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Context_ExposesFrameStore()
    {
        var ctx = new BotExecutionContext();
        Assert.NotNull(ctx.Frames);
    }
}
