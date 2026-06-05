using System.Runtime.InteropServices;

namespace AdbCore.Window;

/// <summary>The live <see cref="IWindowActivator"/>: restores a minimized window then sets it foreground —
/// the same <c>SetForegroundWindow</c> mechanism the input senders use so injected clicks/keys land on it.</summary>
public sealed class Win32WindowActivator : IWindowActivator
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

    public void Activate(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        if (IsIconic(handle)) { ShowWindow(handle, SW_RESTORE); }
        SetForegroundWindow(handle);
    }
}
