using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;

namespace AdbCore.Android;

/// <summary>An <see cref="IAndroidDevice"/> backed by AdvancedSharpAdbClient talking to the ADB server.
/// Thin adapter over shell commands + the Install/GetFrameBuffer API; verified live (real device).</summary>
public sealed class AdvancedSharpAdbDevice : IAndroidDevice
{
    private readonly AdbClient _client;
    private readonly string _serial;
    private DeviceData _device;

    public AdvancedSharpAdbDevice(AdbClient client, DeviceData device)
    {
        _client = client;
        _device = device;
        // Serial is the stable device identity; the transport id captured in DeviceData is not — a reboot
        // makes the ADB server reassign transport ids, orphaning the cached handle. Keep the serial so we
        // can re-resolve a fresh DeviceData when a command fails on a stale handle (see Invoke).
        _serial = device.Serial;
    }

    /// <summary>Runs an operation against the bound device, re-resolving the live <see cref="DeviceData"/>
    /// by serial and retrying once if the cached handle is stale (e.g. after a reboot reassigned the ADB
    /// transport id — "no device with transport id 'N'"). Keeps the no-reboot fast path allocation-free.</summary>
    private T Invoke<T>(Func<DeviceData, T> op)
    {
        try
        {
            return op(_device);
        }
        catch
        {
            _device = _client.GetDevices().FirstOrDefault(d => d.Serial == _serial)
                ?? throw new InvalidOperationException($"ADB device '{_serial}' is not currently connected.");
            return op(_device);
        }
    }

    private void Invoke(Action<DeviceData> op) => Invoke<object?>(d => { op(d); return null; });

    // 3.6.16: AdbClient.ExecuteRemoteCommand(string, DeviceData) — synchronous, no receiver needed.
    private void Shell(string command) => Invoke(d => _client.ExecuteRemoteCommand(command, d));

    public void Tap(int x, int y) => Shell($"input tap {x} {y}");

    public void Swipe(int x1, int y1, int x2, int y2, int durationMs)
        => Shell($"input swipe {x1} {y1} {x2} {y2} {durationMs}");

    public void LongPress(int x, int y, int durationMs) => Shell(AdbInputCommand.LongPress(x, y, durationMs));

    public void SendText(string text) => Shell(AdbInputCommand.Text(text));

    public void KeyEvent(int keyCode, int count) => Shell(AdbInputCommand.KeyEvent(keyCode, count));

    public void PressBack() => Shell("input keyevent 4");

    public void LaunchApp(string package)
        => Shell($"monkey -p {package} -c android.intent.category.LAUNCHER 1");

    /// <summary>Captures the device screen as PNG bytes via the ADB framebuffer API.</summary>
    public byte[] Screenshot()
    {
        // 3.6.16: AdbClient.GetFrameBuffer(DeviceData) returns a Framebuffer already populated with
        // Header (Width, Height, Bpp, Red/Green/Blue/Alpha channel info) and Data (raw pixel bytes).
        using Framebuffer fb = Invoke(d => _client.GetFrameBuffer(d));

        FramebufferHeader hdr = fb.Header;
        byte[] raw = fb.Data ?? throw new InvalidOperationException("ADB framebuffer returned no pixel data.");

        int width = (int)hdr.Width;
        int height = (int)hdr.Height;
        int bpp = (int)hdr.Bpp; // bits per pixel, typically 32 or 24
        int bytesPerPixel = bpp / 8;

        // Channel offsets in the header are in BITS from the start of each pixel.
        int rOff = (int)hdr.Red.Offset / 8;
        int gOff = (int)hdr.Green.Offset / 8;
        int bOff = (int)hdr.Blue.Offset / 8;
        // Alpha may be absent (length == 0); default to fully opaque.
        bool hasAlpha = hdr.Alpha.Length > 0;
        int aOff = hasAlpha ? (int)hdr.Alpha.Offset / 8 : -1;

        // Guard against a short/corrupt frame so a bad capture is a clear error, not an opaque
        // IndexOutOfRangeException deep in the pixel loop below.
        if (raw.Length < (long)width * height * bytesPerPixel)
        {
            throw new InvalidOperationException(
                $"ADB framebuffer is smaller than {width}x{height}@{bpp}bpp ({raw.Length} bytes); the capture was incomplete.");
        }

        // Build a 32-bpp ARGB Bitmap from the raw framebuffer bytes.
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bmpData = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            int stride = bmpData.Stride; // bytes per row in the bitmap (may include padding)
            // Allocate a managed row-buffer and write pixel-by-pixel, converting channel order.
            byte[] row = new byte[stride];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = (y * width + x) * bytesPerPixel;
                    byte r = raw[srcIdx + rOff];
                    byte g = raw[srcIdx + gOff];
                    byte b = raw[srcIdx + bOff];
                    byte a = hasAlpha ? raw[srcIdx + aOff] : (byte)255;

                    // Format32bppArgb in memory (little-endian): B, G, R, A per pixel.
                    int dstIdx = x * 4;
                    row[dstIdx + 0] = b;
                    row[dstIdx + 1] = g;
                    row[dstIdx + 2] = r;
                    row[dstIdx + 3] = a;
                }

                // Copy the filled row into the locked bitmap at the correct stride offset.
                Marshal.Copy(row, 0, bmpData.Scan0 + y * stride, stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Installs an APK from the local file system onto the device.</summary>
    public void InstallApk(string apkPath)
    {
        // 3.6.16: AdbClient.Install(DeviceData, Stream, Action<InstallProgressEventArgs>?, string[])
        using var apk = File.OpenRead(apkPath);
        // Reset to the start on a retry so a re-resolve after a stale-handle failure re-reads the full APK.
        Invoke(d => { apk.Position = 0; _client.Install(d, apk, callback: null); });
    }
}
