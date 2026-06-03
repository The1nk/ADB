using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AdbCore.Targets;

/// <summary>Win32 implementation of <see cref="IWindowEnumerator"/>. Lists visible top-level windows with
/// a non-empty title via <c>EnumWindows</c>, resolving each window's owning process name (best-effort).</summary>
public sealed class Win32WindowEnumerator : IWindowEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>A window qualifies for the list when it is visible and has a non-empty title.</summary>
    public static bool ShouldInclude(bool isVisible, int titleLength) => isVisible && titleLength > 0;

    public IReadOnlyList<WindowInfo> Enumerate()
    {
        var results = new List<WindowInfo>();
        EnumWindows((hWnd, _) =>
        {
            var length = GetWindowTextLength(hWnd);
            if (!ShouldInclude(IsWindowVisible(hWnd), length))
            {
                return true; // skip, keep enumerating
            }

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            results.Add(new WindowInfo(hWnd, sb.ToString(), ResolveProcessName(hWnd)));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    private static string ResolveProcessName(IntPtr hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == 0)
            {
                return string.Empty;
            }

            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty; // process exited between enumeration and access, or access denied
        }
    }
}
