using System.Runtime.InteropServices;

namespace AdbCore.Input;

/// <summary>PostMessage implementation of <see cref="IInputSender"/>: posts messages so a window need not
/// be foreground. Note: some apps and most games ignore synthesized messages, and modifier state (Ctrl/Alt/
/// Shift) set via posted key messages is not seen by GetKeyState — so chords are unreliable here. The
/// foreground SendInput sender is the dependable path.</summary>
public sealed class Win32PostMessageSender : IInputSender
{
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;
    private const int MK_LBUTTON = 0x0001;
    private const int MK_RBUTTON = 0x0002;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;

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

    public void TypeText(IntPtr windowHandle, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var ch in text)
        {
            PostMessage(windowHandle, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
        }
    }

    public void KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control)) PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_CONTROL, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Alt)) PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_MENU, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Shift)) PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_SHIFT, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Win)) PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)VK_LWIN, IntPtr.Zero);

        PostMessage(windowHandle, WM_KEYDOWN, (IntPtr)virtualKey, IntPtr.Zero);
        PostMessage(windowHandle, WM_KEYUP, (IntPtr)virtualKey, IntPtr.Zero);

        if (modifiers.HasFlag(KeyModifiers.Win)) PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_LWIN, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Shift)) PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_SHIFT, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Alt)) PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_MENU, IntPtr.Zero);
        if (modifiers.HasFlag(KeyModifiers.Control)) PostMessage(windowHandle, WM_KEYUP, (IntPtr)VK_CONTROL, IntPtr.Zero);
    }

    // Cast through uint so a y >= 32768 does not sign-extend into the high 32 bits of the IntPtr
    // (matches the Win32 MAKELPARAM macro's unsigned semantics).
    private static IntPtr MakeLParam(int x, int y)
        => (IntPtr)(uint)((y << 16) | (x & 0xFFFF));
}
