using System.Runtime.InteropServices;

namespace AdbCore.Input;

/// <summary>Foreground <see cref="IInputSender"/>: activates the target window and injects real OS input
/// via SendInput (mouse at window-relative coordinates; keyboard as Unicode text or virtual-key presses).
/// Reliable across modern apps, but foreground-only — it moves the real cursor and drives one window at a time.</summary>
public sealed class Win32SendInputSender : IInputSender
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // INPUT is a union: a 4-byte type tag, then (on x64, after 4 bytes padding for 8-byte alignment) the
    // mouse OR keyboard payload overlaid at offset 8. sizeof is 40 on x64, matching the Win32 INPUT struct.
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public MOUSEINPUT mi;
        [FieldOffset(8)] public KEYBDINPUT ki;
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
        Send(Mouse(MOUSEEVENTF_LEFTDOWN), Mouse(MOUSEEVENTF_LEFTUP));
    }

    public void RightClick(IntPtr windowHandle, int clientX, int clientY)
    {
        PositionCursor(windowHandle, clientX, clientY);
        Send(Mouse(MOUSEEVENTF_RIGHTDOWN), Mouse(MOUSEEVENTF_RIGHTUP));
    }

    public void DoubleClick(IntPtr windowHandle, int clientX, int clientY)
    {
        PositionCursor(windowHandle, clientX, clientY);
        // Two down/up pairs in one batch; the OS treats simultaneous injection as within the double-click threshold.
        Send(Mouse(MOUSEEVENTF_LEFTDOWN), Mouse(MOUSEEVENTF_LEFTUP), Mouse(MOUSEEVENTF_LEFTDOWN), Mouse(MOUSEEVENTF_LEFTUP));
    }

    // A bare move must NOT activate the window — a hover shouldn't steal focus; only the clicks activate.
    public void MoveTo(IntPtr windowHandle, int clientX, int clientY)
        => MoveCursor(windowHandle, clientX, clientY);

    public async Task TypeText(IntPtr windowHandle, string text, int keyDelayMs, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text))
        {
            return; // nothing to type — don't steal focus
        }

        SetForegroundWindow(windowHandle);

        // Send each key event in its own SendInput call and pace between them. Batching every
        // down/up into one call floods the target's input queue, dropping KEYUPs and triggering
        // OS auto-repeat (the source of the garbled, repeated-character output).
        foreach (var ch in text)
        {
            Send(Key(0, ch, KEYEVENTF_UNICODE));
            await PaceAsync(keyDelayMs, ct);
            Send(Key(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            await PaceAsync(keyDelayMs, ct);
        }
    }

    public async Task KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers, int keyDelayMs, CancellationToken ct)
    {
        SetForegroundWindow(windowHandle);

        if (modifiers.HasFlag(KeyModifiers.Control)) { Send(Key(VK_CONTROL, 0, 0)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Alt)) { Send(Key(VK_MENU, 0, 0)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Shift)) { Send(Key(VK_SHIFT, 0, 0)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Win)) { Send(Key(VK_LWIN, 0, 0)); await PaceAsync(keyDelayMs, ct); }

        Send(Key(virtualKey, 0, 0));
        await PaceAsync(keyDelayMs, ct);
        Send(Key(virtualKey, 0, KEYEVENTF_KEYUP));
        await PaceAsync(keyDelayMs, ct);

        if (modifiers.HasFlag(KeyModifiers.Win)) { Send(Key(VK_LWIN, 0, KEYEVENTF_KEYUP)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Shift)) { Send(Key(VK_SHIFT, 0, KEYEVENTF_KEYUP)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Alt)) { Send(Key(VK_MENU, 0, KEYEVENTF_KEYUP)); await PaceAsync(keyDelayMs, ct); }
        if (modifiers.HasFlag(KeyModifiers.Control)) { Send(Key(VK_CONTROL, 0, KEYEVENTF_KEYUP)); await PaceAsync(keyDelayMs, ct); }
    }

    private static Task PaceAsync(int delayMs, CancellationToken ct)
        => delayMs > 0 ? Task.Delay(delayMs, ct) : Task.CompletedTask;

    private static INPUT Mouse(uint flags)
        => new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = flags } };

    private static INPUT Key(ushort vk, ushort scan, uint flags)
        => new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } };

    private static void Send(params INPUT[] inputs)
        => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

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
}
