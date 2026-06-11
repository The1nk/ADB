using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Targets;
using BotRunner;
using Xunit;

namespace BotRunner.Tests;

public class RunnerTargetBinderTests
{
    private sealed class FakeWindowResolver : IWindowResolver
    {
        private readonly IntPtr _handle;
        public FakeWindowResolver(IntPtr handle) => _handle = handle;
        public IntPtr Resolve(string selector) => _handle;
    }

    [Fact]
    public async Task BindAsync_Window_ReturnsResolvedHandle()
    {
        var binder = new RunnerTargetBinder(new FakeWindowResolver(new IntPtr(0x1234)));
        var target = new BotTarget { Id = Guid.NewGuid(), Name = "W", Type = BotTargetType.Window, Selector = "title:Game" };

        var resolved = await binder.BindAsync(target, CancellationToken.None);

        Assert.Equal(BotTargetType.Window, resolved.Type);
        Assert.Equal(new IntPtr(0x1234), resolved.Handle);
    }

    [Fact]
    public async Task BindAsync_Window_Unresolved_Throws()
    {
        var binder = new RunnerTargetBinder(new FakeWindowResolver(IntPtr.Zero));
        var target = new BotTarget { Id = Guid.NewGuid(), Name = "W", Type = BotTargetType.Window, Selector = "title:Nope" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => binder.BindAsync(target, CancellationToken.None));
    }
}
