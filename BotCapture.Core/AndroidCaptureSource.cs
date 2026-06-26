using System.Drawing;
using System.IO;
using AdbCore.Android;

namespace BotCapture.Core;

/// <summary>An <see cref="ICaptureSource"/> backed by a connected Android device. Frames come from the ADB
/// framebuffer (<see cref="IAndroidDevice.Screenshot"/>) — the same path the runtime Android Find Image
/// action matches against, so captured templates are device-pixel correct.</summary>
public sealed class AndroidCaptureSource : ICaptureSource
{
    private readonly IAndroidDevice _device;

    public AndroidCaptureSource(string serial, string state, IAndroidDevice device)
    {
        Label = serial;
        SubLabel = state;
        _device = device;
    }

    public string Label { get; }
    public string SubLabel { get; }

    public Bitmap Capture()
    {
        using var ms = new MemoryStream(_device.Screenshot());
        using var decoded = new Bitmap(ms);
        return new Bitmap(decoded); // detached copy so the MemoryStream can be disposed safely
    }
}
