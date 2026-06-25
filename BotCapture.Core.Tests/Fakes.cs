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

internal sealed class FakeTemplateMatcher : AdbCore.Screen.ITemplateMatcher
{
    public AdbCore.Screen.MatchResult? Next;
    public Exception? Throw;
    public string? LastTemplatePath;
    public double LastMinConfidence;

    public AdbCore.Screen.MatchResult? Match(System.Drawing.Bitmap haystack, string templatePath, double minConfidence)
    {
        LastTemplatePath = templatePath;
        LastMinConfidence = minConfidence;
        if (Throw is not null) throw Throw;
        return Next;
    }
}

internal sealed class FakeCaptureSource : ICaptureSource
{
    public string Label { get; set; } = "fake";
    public string SubLabel { get; set; } = "fake";
    public Func<System.Drawing.Bitmap>? Behavior;
    public int CaptureCalls;

    public System.Drawing.Bitmap Capture()
    {
        CaptureCalls++;
        return Behavior is not null ? Behavior() : new System.Drawing.Bitmap(8, 8);
    }
}

internal sealed class FakeAndroidDevice : AdbCore.Android.IAndroidDevice
{
    /// <summary>PNG bytes returned by Screenshot(); defaults to a 6x4 image. Set Throw to simulate a dead device.</summary>
    public byte[]? Png;
    public Exception? Throw;
    public int ScreenshotCalls;

    public byte[] Screenshot()
    {
        ScreenshotCalls++;
        if (Throw is not null) throw Throw;
        if (Png is not null) return Png;
        using var bmp = new System.Drawing.Bitmap(6, 4);
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    public void Tap(int x, int y) { }
    public void Swipe(int x1, int y1, int x2, int y2, int durationMs) { }
    public void PressBack() { }
    public void LaunchApp(string package) { }
    public void InstallApk(string apkPath) { }
}
