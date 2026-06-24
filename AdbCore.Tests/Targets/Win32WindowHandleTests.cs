using AdbCore.Targets;
using Xunit;

namespace AdbCore.Tests.Targets;

public class Win32WindowHandleTests
{
    // -------------------------------------------------------------------------
    // FakeWindowResolver — controllable IsAlive + Resolve with no Win32 calls
    // -------------------------------------------------------------------------
    private sealed class FakeWindowResolver : IWindowResolver
    {
        private readonly bool _isAlive;
        private readonly IntPtr _resolved;

        public string? LastResolvedSelector { get; private set; }

        public FakeWindowResolver(bool isAlive, IntPtr resolved = default)
        {
            _isAlive = isAlive;
            _resolved = resolved;
        }

        public bool IsAlive(IntPtr handle) => _isAlive;

        public IntPtr Resolve(string selector)
        {
            LastResolvedSelector = selector;
            return _resolved;
        }
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GetLiveHandle_AliveHandle_ReturnsCachedHwnd()
    {
        var resolver = new FakeWindowResolver(isAlive: true);
        var sut = new Win32WindowHandle(resolver, "process:Notepad", (IntPtr)0x100);

        var result = sut.GetLiveHandle();

        Assert.Equal((IntPtr)0x100, result);
        Assert.Null(resolver.LastResolvedSelector); // resolver.Resolve was NOT called
    }

    [Fact]
    public void GetLiveHandle_DeadHandle_ReResolvesAndReturnsFreshHwnd()
    {
        // Original handle (0x100) is dead; re-resolved handle (0x200) is alive.
        var resolvedSelector = (string?)null;
        var resolver = new StatefulResolver(
            isAlive: hwnd => hwnd == (IntPtr)0x200,
            resolve: sel => { resolvedSelector = sel; return (IntPtr)0x200; });
        var sut = new Win32WindowHandle(resolver, "process:Notepad", (IntPtr)0x100);

        var result = sut.GetLiveHandle();

        Assert.Equal((IntPtr)0x200, result);
        Assert.Equal("process:Notepad", resolvedSelector);
    }

    [Fact]
    public void GetLiveHandle_DeadHandle_CachesFreshHwnd_SubsequentCallDoesNotReResolve()
    {
        // First call re-resolves because the cached handle is dead, but after that the
        // fresh handle is cached — subsequent calls must use the cache.
        var callCount = 0;
        var statefulResolver = new StatefulResolver(
            isAlive: hwnd => hwnd == (IntPtr)0x200, // fresh handle is alive, original is dead
            resolve: _ => { callCount++; return (IntPtr)0x200; });

        var sut = new Win32WindowHandle(statefulResolver, "process:Notepad", (IntPtr)0x100);

        var first = sut.GetLiveHandle();   // triggers re-resolution
        var second = sut.GetLiveHandle();  // should use cached (IntPtr)0x200

        Assert.Equal((IntPtr)0x200, first);
        Assert.Equal((IntPtr)0x200, second);
        Assert.Equal(1, callCount); // Resolve called exactly once
    }

    [Fact]
    public void GetLiveHandle_DeadHandle_ReResolutionReturnsZero_ThrowsInvalidOperationException()
    {
        var resolver = new FakeWindowResolver(isAlive: false, resolved: IntPtr.Zero);
        var sut = new Win32WindowHandle(resolver, "title:Game", (IntPtr)0x100);

        var ex = Assert.Throws<InvalidOperationException>(() => sut.GetLiveHandle());
        Assert.Contains("title:Game", ex.Message);
        Assert.Contains("no longer available", ex.Message);
    }

    [Fact]
    public void GetLiveHandle_DeadHandle_ReResolvedHandleFailsIsAlive_ThrowsInvalidOperationException()
    {
        // Reproduce the hwnd:<literal> bug: Resolve returns a non-zero handle but IsAlive
        // always returns false (e.g. the literal HWND points to a dead window). The re-resolved
        // handle must NOT be cached and returned — it must be treated as failure.
        var resolver = new StatefulResolver(
            isAlive: _ => false,           // every handle is dead, including the re-resolved one
            resolve: _ => (IntPtr)0x100);  // Resolve returns a non-zero handle

        var sut = new Win32WindowHandle(resolver, "hwnd:0x100", (IntPtr)0x100);

        var ex = Assert.Throws<InvalidOperationException>(() => sut.GetLiveHandle());
        Assert.Contains("hwnd:0x100", ex.Message);
        Assert.Contains("no longer available", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Inline stateful resolver for the caching test
    // -------------------------------------------------------------------------
    private sealed class StatefulResolver(Func<IntPtr, bool> isAlive, Func<string, IntPtr> resolve) : IWindowResolver
    {
        public bool IsAlive(IntPtr handle) => isAlive(handle);
        public IntPtr Resolve(string selector) => resolve(selector);
    }
}
