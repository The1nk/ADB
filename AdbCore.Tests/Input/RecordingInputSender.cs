using AdbCore.Input;

namespace AdbCore.Tests.Input;

/// <summary>Test double that records the last input operation and its arguments.</summary>
internal sealed class RecordingInputSender : IInputSender
{
    public string? LastOp { get; private set; }
    public IntPtr LastWindow { get; private set; }
    public int LastX { get; private set; }
    public int LastY { get; private set; }
    public string? LastText { get; private set; }
    public ushort LastVk { get; private set; }
    public KeyModifiers LastModifiers { get; private set; }
    public int Calls { get; private set; }

    public void Click(IntPtr windowHandle, int clientX, int clientY) => RecordMouse("Click", windowHandle, clientX, clientY);
    public void RightClick(IntPtr windowHandle, int clientX, int clientY) => RecordMouse("RightClick", windowHandle, clientX, clientY);
    public void DoubleClick(IntPtr windowHandle, int clientX, int clientY) => RecordMouse("DoubleClick", windowHandle, clientX, clientY);
    public void MoveTo(IntPtr windowHandle, int clientX, int clientY) => RecordMouse("MoveTo", windowHandle, clientX, clientY);

    public void TypeText(IntPtr windowHandle, string text)
    {
        LastOp = "TypeText";
        LastWindow = windowHandle;
        LastText = text;
        Calls++;
    }

    public void KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers)
    {
        LastOp = "KeyPress";
        LastWindow = windowHandle;
        LastVk = virtualKey;
        LastModifiers = modifiers;
        Calls++;
    }

    private void RecordMouse(string op, IntPtr windowHandle, int x, int y)
    {
        LastOp = op;
        LastWindow = windowHandle;
        LastX = x;
        LastY = y;
        Calls++;
    }
}
