using System.Runtime.InteropServices;

namespace AdbCore.Input;

/// <summary>Win32 implementation of <see cref="IInputSender"/> using PostMessage so a window need not be
/// foreground. Coordinates are client-relative and packed into the message lParam. Note: some apps and
/// most games ignore synthesized messages — for those, a foreground SendInput sender would be needed.</summary>
public sealed class Win32InputSender : IInputSender
{
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public void Click(IntPtr windowHandle, int clientX, int clientY)
    {
        var lParam = MakeLParam(clientX, clientY);
        PostMessage(windowHandle, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
        PostMessage(windowHandle, WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    // Cast through uint so a y >= 32768 does not sign-extend into the high 32 bits of the IntPtr
    // (matches the Win32 MAKELPARAM macro's unsigned semantics).
    private static IntPtr MakeLParam(int x, int y)
        => (IntPtr)(uint)((y << 16) | (x & 0xFFFF));
}
