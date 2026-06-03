using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AdbCore.Screen;

/// <summary>Foreground/standard capture via PrintWindow (PW_RENDERFULLCONTENT) with a screen-region
/// BitBlt fallback when PrintWindow yields a blank frame; or forced BitBlt. Captures the client area.</summary>
public sealed class Win32WindowCapture : IWindowCapture
{
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);

    public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method)
    {
        GetClientRect(windowHandle, out var client);
        var clientWidth = Math.Max(1, client.Right - client.Left);
        var clientHeight = Math.Max(1, client.Bottom - client.Top);

        if (method == ScreenCaptureMethod.BitBlt)
        {
            return CaptureViaScreenBitBlt(windowHandle, clientWidth, clientHeight);
        }

        // PrintWindow(PW_RENDERFULLCONTENT) renders the WHOLE window (incl. title bar/borders) from its
        // top-left, so capture at window size then crop to the client area.
        GetWindowRect(windowHandle, out var win);
        var winWidth = Math.Max(1, win.Right - win.Left);
        var winHeight = Math.Max(1, win.Bottom - win.Top);

        var full = new Bitmap(winWidth, winHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(full))
        {
            var hdc = g.GetHdc();
            try
            {
                PrintWindow(windowHandle, hdc, PW_RENDERFULLCONTENT);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        if (IsBlank(full))
        {
            full.Dispose();
            return CaptureViaScreenBitBlt(windowHandle, clientWidth, clientHeight);
        }

        // Crop the client area out of the full-window capture (offset = client origin - window origin, in screen px).
        var clientOrigin = new POINT { X = 0, Y = 0 };
        ClientToScreen(windowHandle, ref clientOrigin);
        var offsetX = Math.Clamp(clientOrigin.X - win.Left, 0, winWidth - 1);
        var offsetY = Math.Clamp(clientOrigin.Y - win.Top, 0, winHeight - 1);
        var cropWidth = Math.Min(clientWidth, winWidth - offsetX);
        var cropHeight = Math.Min(clientHeight, winHeight - offsetY);

        using (full)
        {
            return full.Clone(new Rectangle(offsetX, offsetY, cropWidth, cropHeight), full.PixelFormat);
        }
    }

    private static Bitmap CaptureViaScreenBitBlt(IntPtr windowHandle, int width, int height)
    {
        var origin = new POINT { X = 0, Y = 0 };
        ClientToScreen(windowHandle, ref origin);

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        var destHdc = g.GetHdc();
        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            BitBlt(destHdc, 0, 0, width, height, screenDc, origin.X, origin.Y, SRCCOPY);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            g.ReleaseHdc(destHdc);
        }

        return bmp;
    }

    // Sample a handful of pixels; PrintWindow on GPU-composited windows returns an all-zero (transparent/black) frame.
    private static bool IsBlank(Bitmap bmp)
    {
        var first = bmp.GetPixel(0, 0);
        for (var i = 0; i < 5; i++)
        {
            var p = bmp.GetPixel((bmp.Width - 1) * i / 4, (bmp.Height - 1) * i / 4);
            if (p.ToArgb() != first.ToArgb())
            {
                return false;
            }
        }

        return first.A == 0 || (first.R == 0 && first.G == 0 && first.B == 0);
    }
}
