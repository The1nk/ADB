using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ScreenOcrRegistrationTests
{
    [Theory]
    [InlineData("screen.readText")]
    [InlineData("screen.findText")]
    [InlineData("screen.waitForText")]
    [InlineData("screen.assertTextAbsent")]
    public void ScreenOcrAction_IsRegistered(string typeKey)
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        Assert.True(defs.TryGet(typeKey, out _));
        Assert.True(execs.TryGet(typeKey, out var exec) && exec is not null);
    }
}
