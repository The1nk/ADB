using AdbCore.Actions.BuiltIn;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ParallelDefinitionsTests
{
    [Fact]
    public void RunParallel_Definition_HasBranchPortsAndStrategyConfig()
    {
        var def = new RunParallelAction();

        Assert.Equal("control.runParallel", def.TypeKey);
        Assert.Equal("Control Flow", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "branch1", "branch2" }, def.OutputPorts.Select(p => p.Name));
        Assert.False(def.SupportsRetry);

        Assert.Contains(def.ConfigFields, f => f.Key == RunParallelAction.BranchesKey);
        var strategy = def.ConfigFields.Single(f => f.Key == RunParallelAction.OnBranchFailureKey);
        Assert.Equal(new[] { "HaltAll", "WaitThenHalt", "Continue" }, strategy.Options);
    }

    [Fact]
    public void RunParallel_BranchPort_FormatsOneBasedName()
    {
        Assert.Equal("branch1", RunParallelAction.BranchPort(1));
        Assert.Equal("branch3", RunParallelAction.BranchPort(3));
    }

    [Fact]
    public void Join_Definition_HasAllSucceededAndSomeFailedPorts()
    {
        var def = new JoinAction();

        Assert.Equal("control.join", def.TypeKey);
        Assert.Equal("Control Flow", def.Category);
        Assert.Equal(new[] { "in" }, def.InputPorts.Select(p => p.Name));
        Assert.Equal(new[] { "allSucceeded", "someFailed" }, def.OutputPorts.Select(p => p.Name));
        Assert.False(def.SupportsRetry);
    }
}
