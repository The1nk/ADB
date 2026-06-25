using System.Drawing;
using AdbCore.Screen;
using AdbCore.Targets;

namespace BotCapture.Core;

/// <summary>An <see cref="ICaptureSource"/> backed by a Win32 window handle.</summary>
public sealed class WindowCaptureSource : ICaptureSource
{
    private readonly WindowInfo _info;
    private readonly IWindowCapture _capture;

    public WindowCaptureSource(WindowInfo info, IWindowCapture capture)
    {
        _info = info;
        _capture = capture;
    }

    public string Label => _info.Title;
    public string SubLabel => _info.ProcessName;

    public Bitmap Capture() => _capture.Capture(_info.Handle, ScreenCaptureMethod.Auto);
}
