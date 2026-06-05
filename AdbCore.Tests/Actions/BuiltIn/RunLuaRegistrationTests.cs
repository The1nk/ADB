using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class RunLuaRegistrationTests
{
    [Fact]
    public void RunLua_IsRegistered()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);
        Assert.True(defs.TryGet("scripting.runLua", out _));
        Assert.True(execs.TryGet("scripting.runLua", out var e) && e is not null);
    }
}
