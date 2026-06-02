using AdbCore.Input;

namespace AdbCore.Tests.Input;

/// <summary>Test double that records the last pointer operation and its arguments.</summary>
internal sealed class RecordingInputSender : IInputSender
{
    public string? LastOp { get; private set; }
    public IntPtr LastWindow { get; private set; }
    public int LastX { get; private set; }
    public int LastY { get; private set; }
    public int Calls { get; private set; }

    public void Click(IntPtr windowHandle, int clientX, int clientY) => Record("Click", windowHandle, clientX, clientY);
    public void RightClick(IntPtr windowHandle, int clientX, int clientY) => Record("RightClick", windowHandle, clientX, clientY);
    public void DoubleClick(IntPtr windowHandle, int clientX, int clientY) => Record("DoubleClick", windowHandle, clientX, clientY);
    public void MoveTo(IntPtr windowHandle, int clientX, int clientY) => Record("MoveTo", windowHandle, clientX, clientY);

    private void Record(string op, IntPtr windowHandle, int x, int y)
    {
        LastOp = op;
        LastWindow = windowHandle;
        LastX = x;
        LastY = y;
        Calls++;
    }
}
