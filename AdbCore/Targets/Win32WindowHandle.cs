namespace AdbCore.Targets;

/// <summary>An <see cref="IWindowHandle"/> backed by a Win32 HWND. Validates the cached HWND via
/// <c>IsWindow</c> before each use and re-resolves from the selector when the window has been
/// destroyed and recreated (e.g. after an in-game restart).</summary>
public sealed class Win32WindowHandle : IWindowHandle
{
    private readonly IWindowResolver _resolver;
    private readonly string _selector;
    private IntPtr _cached;

    /// <param name="resolver">Resolver used to re-resolve when the cached HWND is dead.</param>
    /// <param name="selector">The original selector string (e.g. <c>process:Notepad</c>).</param>
    /// <param name="initialHandle">The HWND resolved at bind time.</param>
    public Win32WindowHandle(IWindowResolver resolver, string selector, IntPtr initialHandle)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(selector);
        _resolver = resolver;
        _selector = selector;
        _cached = initialHandle;
    }

    /// <inheritdoc/>
    public IntPtr GetLiveHandle()
    {
        if (_resolver.IsAlive(_cached))
        {
            return _cached;
        }

        // Cached HWND is dead — attempt a single re-resolution from the original selector.
        var fresh = _resolver.Resolve(_selector);
        if (fresh == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Window target '{_selector}' is no longer available (it may have been closed).");
        }

        _cached = fresh;
        return _cached;
    }
}
