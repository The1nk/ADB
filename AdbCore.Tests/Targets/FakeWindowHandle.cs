using AdbCore.Targets;

namespace AdbCore.Tests.Targets;

/// <summary>A controllable <see cref="IWindowHandle"/> for use in unit tests. Always returns
/// <see cref="Handle"/> from <see cref="GetLiveHandle"/> — no real Win32 calls.</summary>
internal sealed class FakeWindowHandle(IntPtr handle) : IWindowHandle
{
    public IntPtr Handle { get; set; } = handle;

    public IntPtr GetLiveHandle() => Handle;
}
