using AdbCore.Screen;
using AdbCore.Targets;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class CaptureSourceTests
{
    [Fact]
    public void Window_Capture_UsesHandleAndAutoMethod_AndExposesInfo()
    {
        var capture = new FakeWindowCapture();
        var src = new WindowCaptureSource(new WindowInfo((IntPtr)42, "Game", "game.exe"), capture);

        using var bmp = src.Capture();

        Assert.Equal("Game", src.Label);
        Assert.Equal("game.exe", src.SubLabel);
        Assert.Equal((IntPtr)42, capture.Calls[^1].Handle);
        Assert.Equal(ScreenCaptureMethod.Auto, capture.Calls[^1].Method);
        Assert.NotNull(bmp);
    }

    [Fact]
    public void Android_Capture_DecodesScreenshotPng_AndExposesSerialAndState()
    {
        var device = new FakeAndroidDevice(); // default 6x4 PNG
        var src = new AndroidCaptureSource("emulator-5554", "device", device);

        using var bmp = src.Capture();

        Assert.Equal("emulator-5554", src.Label);
        Assert.Equal("device", src.SubLabel);
        Assert.Equal(6, bmp.Width);
        Assert.Equal(4, bmp.Height);
        Assert.Equal(1, device.ScreenshotCalls);
    }
}
