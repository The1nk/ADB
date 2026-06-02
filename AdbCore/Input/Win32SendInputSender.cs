using System.Runtime.InteropServices;

namespace AdbCore.Input;

/// <summary>Foreground <see cref="IInputSender"/>: brings the target window to the front, moves the cursor
/// to the window-relative point (converted to screen coordinates), and injects real OS mouse input via
/// SendInput. Reliable across modern apps, but foreground-only — it moves the real cursor and drives one
/// window at a time.</summary>
public sealed class Win32SendInputSender : IInputSender
{
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // INPUT is a union of mouse/keyboard/hardware input. MOUSEINPUT is the largest relevant member,
    // so this layout marshals to the correct sizeof(INPUT) on x64 for mouse events.
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    public void Click(IntPtr windowHandle, int clientX, int clientY)
    {
        PositionCursor(windowHandle, clientX, clientY);
        InjectMouse(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
    }

    public void RightClick(IntPtr windowHandle, int clientX, int clientY)
    {
        PositionCursor(windowHandle, clientX, clientY);
        InjectMouse(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
    }

    public void DoubleClick(IntPtr windowHandle, int clientX, int clientY)
    {
        PositionCursor(windowHandle, clientX, clientY);
        // Two down/up pairs in one batch; the OS treats simultaneous injection as within the double-click threshold.
        InjectMouse(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
    }

    // A bare move must NOT activate the window — a hover shouldn't steal focus; only the clicks activate.
    public void MoveTo(IntPtr windowHandle, int clientX, int clientY)
        => MoveCursor(windowHandle, clientX, clientY);

    private static void PositionCursor(IntPtr windowHandle, int clientX, int clientY)
    {
        SetForegroundWindow(windowHandle); // activate the target so the injected click lands on it
        MoveCursor(windowHandle, clientX, clientY);
    }

    private static void MoveCursor(IntPtr windowHandle, int clientX, int clientY)
    {
        var point = new POINT { X = clientX, Y = clientY };
        ClientToScreen(windowHandle, ref point);
        SetCursorPos(point.X, point.Y);
    }

    private static void InjectMouse(params uint[] flags)
    {
        var inputs = new INPUT[flags.Length];
        for (var i = 0; i < flags.Length; i++)
        {
            inputs[i] = new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = flags[i] } };
        }

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
