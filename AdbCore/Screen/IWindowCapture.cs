using System.Drawing;

namespace AdbCore.Screen;

/// <summary>Captures a target window's client area into a bitmap. The caller owns and disposes the result.</summary>
public interface IWindowCapture
{
    Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method);
}
