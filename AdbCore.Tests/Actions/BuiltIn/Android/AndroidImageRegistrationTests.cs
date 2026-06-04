using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidImageRegistrationTests
{
    [Theory]
    [InlineData("android.findImage")]
    [InlineData("android.waitForImage")]
    [InlineData("android.assertImageAbsent")]
    public void AndroidImageAction_IsRegistered_AsDefinitionAndExecutor(string typeKey)
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        Assert.True(defs.TryGet(typeKey, out _));
        Assert.True(execs.TryGet(typeKey, out var exec) && exec is not null);
    }
}
