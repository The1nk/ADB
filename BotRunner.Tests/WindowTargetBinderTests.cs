using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Targets;
using BotRunner;
using Xunit;

namespace BotRunner.Tests;

public class WindowTargetBinderTests
{
    private sealed class FakeWindowResolver : IWindowResolver
    {
        private readonly IntPtr _result;
        public string? LastSelector { get; private set; }
        public FakeWindowResolver(IntPtr result) => _result = result;
        public IntPtr Resolve(string selector) { LastSelector = selector; return _result; }
    }

    [Fact]
    public void Bind_SetsHandleOnWindowTargets()
    {
        var id = Guid.NewGuid();
        var targets = new Dictionary<Guid, ResolvedTarget>
        {
            [id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "process:Notepad" },
        };
        var resolver = new FakeWindowResolver((IntPtr)777);

        WindowTargetBinder.Bind(targets, resolver);

        Assert.Equal((IntPtr)777, targets[id].Handle);
        Assert.Equal("process:Notepad", resolver.LastSelector);
    }

    [Fact]
    public void Bind_LeavesNonWindowTargetsUntouched()
    {
        var id = Guid.NewGuid();
        var targets = new Dictionary<Guid, ResolvedTarget>
        {
            [id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:emulator-5554" },
        };

        WindowTargetBinder.Bind(targets, new FakeWindowResolver((IntPtr)1));

        Assert.Null(targets[id].Handle);
    }

    [Fact]
    public void Bind_UnresolvableWindow_ThrowsCommandLineException()
    {
        var id = Guid.NewGuid();
        var targets = new Dictionary<Guid, ResolvedTarget>
        {
            [id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "process:Ghost" },
        };

        var ex = Assert.Throws<CommandLineException>(
            () => WindowTargetBinder.Bind(targets, new FakeWindowResolver(IntPtr.Zero)));
        Assert.Contains("process:Ghost", ex.Message);
    }
}
