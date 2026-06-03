using System.Runtime.InteropServices;

namespace BotRunner;

/// <summary>Process DPI awareness for accurate screen capture + cursor positioning. The runner captures
/// and clicks DPI-aware target windows; without per-monitor awareness, on a display scaled above 100%
/// Windows virtualizes the runner's coordinates so the capture's pixel space and <c>SetCursorPos</c>'s
/// pixel space disagree, and clicks drift down-and-right of the matched location.</summary>
internal static class NativeDpi
{
    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 — a sentinel handle value, not a real pointer.
    private static readonly IntPtr PerMonitorAwareV2 = (IntPtr)(-4);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary>Opts the process into Per-Monitor-V2 DPI awareness so every Win32 call
    /// (GetClientRect/ClientToScreen/PrintWindow/BitBlt/SetCursorPos) works in the same physical-pixel
    /// space as the target app. Best-effort: must run before any DPI-sensitive call; ignored on an OS
    /// that doesn't support it (capture/click still align at 100% scaling).</summary>
    public static void EnsurePerMonitorV2()
    {
        try
        {
            SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        }
        catch
        {
            // Pre-Windows-10-1703 (or awareness already fixed by a host): fall back to the process default.
        }
    }
}
