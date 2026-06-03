namespace AdbCore.Targets;

/// <summary>A visible top-level window discovered by <see cref="IWindowEnumerator"/>. <see cref="Handle"/>
/// is the HWND, <see cref="Title"/> the window text, and <see cref="ProcessName"/> the owning process
/// (empty when it can't be resolved).</summary>
public readonly record struct WindowInfo(IntPtr Handle, string Title, string ProcessName);
