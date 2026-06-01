using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ActionResultTests
{
    [Fact]
    public void Ok_SetsSuccessAndPort()
    {
        var r = ActionResult.Ok("out");

        Assert.True(r.Success);
        Assert.Equal("out", r.OutputPort);
        Assert.Null(r.ErrorMessage);
        Assert.NotNull(r.Outputs);
    }

    [Fact]
    public void Fail_SetsErrorAndNotSuccess()
    {
        var r = ActionResult.Fail("boom");

        Assert.False(r.Success);
        Assert.Equal("boom", r.ErrorMessage);
    }
}
