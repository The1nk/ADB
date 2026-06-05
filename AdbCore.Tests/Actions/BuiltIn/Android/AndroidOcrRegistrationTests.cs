using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidOcrRegistrationTests
{
    [Theory]
    [InlineData("android.readText")]
    [InlineData("android.findText")]
    [InlineData("android.waitForText")]
    [InlineData("android.assertTextAbsent")]
    public void AndroidOcrAction_IsRegistered(string typeKey)
    {
        var defs = new ActionRegistry();
        var execs = new ActionExecutorRegistry();
        BuiltInActions.Register(defs, execs);

        Assert.True(defs.TryGet(typeKey, out _));
        Assert.True(execs.TryGet(typeKey, out var exec) && exec is not null);
    }
}
