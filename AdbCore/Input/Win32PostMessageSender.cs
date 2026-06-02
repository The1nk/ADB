using System.Runtime.InteropServices;

namespace AdbCore.Input;

/// <summary>PostMessage implementation of <see cref="IInputSender"/>: posts messages so a window need not
/// be foreground. Coordinates are client-relative and packed into the message lParam. Note: some apps and
/// most games ignore synthesized messages — for those, the foreground SendInput sender is needed.</summary>
public sealed class Win32PostMessageSender : IInputSender
{
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const int MK_LBUTTON = 0x0001;
    private const int MK_RBUTTON = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public void Click(IntPtr windowHandle, int clientX, int clientY)
    {
        var lParam = MakeLParam(clientX, clientY);
        PostMessage(windowHandle, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
        PostMessage(windowHandle, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    public void RightClick(IntPtr windowHandle, int clientX, int clientY)
    {
        var lParam = MakeLParam(clientX, clientY);
        PostMessage(windowHandle, WM_RBUTTONDOWN, (IntPtr)MK_RBUTTON, lParam);
        PostMessage(windowHandle, WM_RBUTTONUP, IntPtr.Zero, lParam);
    }

    public void DoubleClick(IntPtr windowHandle, int clientX, int clientY)
    {
        var lParam = MakeLParam(clientX, clientY);
        PostMessage(windowHandle, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
        PostMessage(windowHandle, WM_LBUTTONUP, IntPtr.Zero, lParam);
        PostMessage(windowHandle, WM_LBUTTONDBLCLK, (IntPtr)MK_LBUTTON, lParam);
        PostMessage(windowHandle, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    public void MoveTo(IntPtr windowHandle, int clientX, int clientY)
        => PostMessage(windowHandle, WM_MOUSEMOVE, IntPtr.Zero, MakeLParam(clientX, clientY));

    // Cast through uint so a y >= 32768 does not sign-extend into the high 32 bits of the IntPtr
    // (matches the Win32 MAKELPARAM macro's unsigned semantics).
    private static IntPtr MakeLParam(int x, int y)
        => (IntPtr)(uint)((y << 16) | (x & 0xFFFF));
}
