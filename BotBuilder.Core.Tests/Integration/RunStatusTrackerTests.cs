using BotBuilder.Core;
using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class RunStatusTrackerTests
{
    private static RunLogEntry Action(string actionId, bool success)
        => new(RunLogKind.Action, actionId, "label", success, success ? null : "err", null, "raw");

    private static RunLogEntry RunStart() => new(RunLogKind.RunStart, null, null, null, null, null, "raw");
    private static RunLogEntry RunEnd(bool success) => new(RunLogKind.RunEnd, null, null, success, null, null, "raw");

    [Fact]
    public void Reset_IsIdleAndEmpty()
    {
        var t = new RunStatusTracker();
        t.Apply(RunStart());
        t.Reset();

        Assert.Equal(RunStatus.Idle, t.Status);
        Assert.Empty(t.NodeStates);
    }

    [Fact]
    public void RunStart_SetsRunning_ClearsNodeStates()
    {
        var t = new RunStatusTracker();
        var id = Guid.NewGuid();
        t.Apply(Action(id.ToString(), success: true));

        t.Apply(RunStart());

        Assert.Equal(RunStatus.Running, t.Status);
        Assert.Empty(t.NodeStates);
    }

    [Fact]
    public void Action_Success_MarksNodeSucceeded_ReturnsId()
    {
        var t = new RunStatusTracker();
        var id = Guid.NewGuid();

        var changed = t.Apply(Action(id.ToString(), success: true));

        Assert.Equal(id, changed);
        Assert.Equal(NodeRunState.Succeeded, t.NodeStates[id]);
    }

    [Fact]
    public void Action_Failure_MarksNodeFailed()
    {
        var t = new RunStatusTracker();
        var id = Guid.NewGuid();

        t.Apply(Action(id.ToString(), success: false));

        Assert.Equal(NodeRunState.Failed, t.NodeStates[id]);
    }

    [Fact]
    public void Action_UnparseableId_IgnoredReturnsNull()
    {
        var t = new RunStatusTracker();

        var changed = t.Apply(Action("not-a-guid", success: true));

        Assert.Null(changed);
        Assert.Empty(t.NodeStates);
    }

    [Theory]
    [InlineData(true, RunStatus.Succeeded)]
    [InlineData(false, RunStatus.Failed)]
    public void RunEnd_SetsOverallStatus(bool success, RunStatus expected)
    {
        var t = new RunStatusTracker();
        t.Apply(RunStart());

        t.Apply(RunEnd(success));

        Assert.Equal(expected, t.Status);
    }

    [Fact]
    public void NonActionEntries_DoNotChangeNodeStates()
    {
        var t = new RunStatusTracker();
        t.Apply(RunStart());

        t.Apply(new RunLogEntry(RunLogKind.Message, null, null, null, null, "hi", "raw"));
        t.Apply(new RunLogEntry(RunLogKind.Unparsed, null, null, null, null, null, "garbage"));

        Assert.Empty(t.NodeStates);
    }
}
