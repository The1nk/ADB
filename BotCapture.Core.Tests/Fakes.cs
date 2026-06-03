using System.Drawing;
using AdbCore.Screen;
using AdbCore.Targets;

namespace BotCapture.Core.Tests;

internal sealed class FakeWindowEnumerator : IWindowEnumerator
{
    public IReadOnlyList<WindowInfo> Result = Array.Empty<WindowInfo>();
    public IReadOnlyList<WindowInfo> Enumerate() => Result;
}

internal sealed class FakeWindowCapture : IWindowCapture
{
    public List<(IntPtr Handle, ScreenCaptureMethod Method)> Calls = new();

    /// <summary>Optional per-call behavior; default returns a tiny bitmap. Set to throw to simulate
    /// an unrenderable window.</summary>
    public Func<IntPtr, Bitmap>? Behavior;

    public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method)
    {
        Calls.Add((windowHandle, method));
        return Behavior is not null ? Behavior(windowHandle) : new Bitmap(8, 8);
    }
}
