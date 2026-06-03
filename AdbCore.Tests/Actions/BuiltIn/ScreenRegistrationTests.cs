using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ScreenRegistrationTests
{
    [Fact]
    public void FindImage_IsRegistered_AsDefinitionAndExecutor()
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        Assert.True(defs.TryGet("screen.findImage", out _));
        Assert.True(execs.TryGet("screen.findImage", out var exec) && exec is not null);
    }

    [Theory]
    [InlineData("screen.findImage")]
    [InlineData("screen.waitForImage")]
    [InlineData("screen.assertImageAbsent")]
    [InlineData("screen.screenshot")]
    public void ScreenAction_IsRegistered_AsDefinitionAndExecutor(string typeKey)
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        Assert.True(defs.TryGet(typeKey, out _));
        Assert.True(execs.TryGet(typeKey, out var exec) && exec is not null);
    }
}
