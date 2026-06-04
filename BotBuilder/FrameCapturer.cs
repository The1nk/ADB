using System.Drawing;
using System.IO;
using System.Linq;
using AdbCore.Android;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Targets;
using AdvancedSharpAdbClient;

namespace BotBuilder;

/// <summary>Captures a still frame of a coordinate-pick target at authoring time. Window targets are
/// resolved to an HWND and client-captured; Android targets are resolved to a device and framebuffer-grabbed.
/// Returns null and an explanatory message on any failure (target not running, no ADB server, bad selector).
/// Live adapter — mirrors the runner's WindowTargetBinder/AndroidTargetBinder; verified by hand.</summary>
public sealed class FrameCapturer
{
    private readonly IWindowResolver _windowResolver = new Win32WindowResolver();
    private readonly IWindowCapture _windowCapture = new Win32WindowCapture();

    public Bitmap? TryCapture(BotTargetType type, string selector, out string? error)
    {
        error = null;
        try
        {
            return type switch
            {
                BotTargetType.Window => CaptureWindow(selector, out error),
                BotTargetType.AndroidDevice => CaptureAndroid(selector, out error),
                _ => Fail($"Coordinate picking isn't supported for {type} targets.", out error),
            };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private Bitmap? CaptureWindow(string selector, out string? error)
    {
        error = null;
        var hwnd = _windowResolver.Resolve(selector);
        if (hwnd == IntPtr.Zero)
        {
            error = $"Couldn't find a window for '{selector}'. Make sure it's running, then try again.";
            return null;
        }

        return _windowCapture.Capture(hwnd, ScreenCaptureMethod.Auto);
    }

    private static Bitmap? CaptureAndroid(string selector, out string? error)
    {
        error = null;
        var serial = AdbSelector.ParseSerial(selector);
        if (serial is null)
        {
            error = $"Android selector must be in the form 'serial:<device>' (got '{selector}').";
            return null;
        }

        AdbServer.Instance.StartServer(adbPath: "adb", restartServerIfNewer: false);
        var client = new AdbClient();
        var device = client.GetDevices().FirstOrDefault(d => d.Serial == serial);
        if (device is null)
        {
            error = $"No connected Android device with serial '{serial}'. Run `adb devices` to check.";
            return null;
        }

        var png = new AdvancedSharpAdbDevice(client, device).Screenshot();
        using var ms = new MemoryStream(png);
        using var decoded = new Bitmap(ms);
        return new Bitmap(decoded); // detached copy so the MemoryStream can be disposed safely
    }

    private static Bitmap? Fail(string message, out string? error)
    {
        error = message;
        return null;
    }
}
