using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ActivateWindowRegistrationTests
{
    [Fact]
    public void ActivateWindow_IsRegistered()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);
        Assert.True(defs.TryGet("window.activate", out _));
        Assert.True(execs.TryGet("window.activate", out var e) && e is not null);
    }
}
