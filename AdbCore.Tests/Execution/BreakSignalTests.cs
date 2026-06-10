using AdbCore.Execution;
using Xunit;

namespace AdbCore.Tests.Execution;

public class BreakSignalTests
{
    [Fact]
    public void WalkOutcome_Break_IsSuccessButFlaggedBreak()
    {
        var outcome = WalkOutcome.Break();
        Assert.True(outcome.Success);   // a break is not a failure
        Assert.True(outcome.IsBreak);
    }

    [Fact]
    public void WalkOutcome_Completed_IsNotBreak()
    {
        Assert.False(WalkOutcome.Completed().IsBreak);
    }

    [Fact]
    public void ControlFlowResult_Break_CarriesBreakOutcome()
    {
        var result = ControlFlowResult.Break();
        Assert.True(result.IsBreak);
        Assert.True(result.Outcome.IsBreak);
        Assert.Null(result.Next);
    }

    [Fact]
    public void ControlFlowResult_ContinueAndHalt_AreNotBreak()
    {
        Assert.False(ControlFlowResult.Continue(null).IsBreak);
        Assert.False(ControlFlowResult.Halt(WalkOutcome.Failed("x", System.Guid.NewGuid())).IsBreak);
    }
}
