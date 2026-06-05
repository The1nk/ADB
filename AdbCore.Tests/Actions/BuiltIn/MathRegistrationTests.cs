using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class MathRegistrationTests
{
    [Fact]
    public void Math_IsRegistered()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);
        Assert.True(defs.TryGet("data.math", out _));
        Assert.True(execs.TryGet("data.math", out var e) && e is not null);
    }
}
