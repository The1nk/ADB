namespace AdbCore.Targets;

/// <summary>A smart window handle that can proactively re-resolve itself from the original selector
/// when the cached HWND is stale. Actions call <see cref="GetLiveHandle"/> instead of holding a
/// bare <see cref="IntPtr"/> so that a window close/reopen during a run is recovered transparently.</summary>
public interface IWindowHandle
{
    /// <summary>Returns the live HWND for this window target. If the cached handle is no longer alive,
    /// re-resolves via the original selector. Throws <see cref="InvalidOperationException"/> if
    /// re-resolution also fails (i.e. the window is truly gone).</summary>
    IntPtr GetLiveHandle();
}
