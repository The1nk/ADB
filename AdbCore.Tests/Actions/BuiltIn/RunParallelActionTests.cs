using System.Linq;
using AdbCore.Actions.BuiltIn;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class RunParallelActionTests
{
    [Fact]
    public void OutputPortsForBranches_BuildsNamedBranchPorts()
    {
        var ports = RunParallelAction.OutputPortsForBranches(3);

        Assert.Equal(new[] { "branch1", "branch2", "branch3" }, ports.Select(p => p.Name));
        Assert.Equal(new[] { "Branch 1", "Branch 2", "Branch 3" }, ports.Select(p => p.Label));
    }

    [Fact]
    public void DefaultOutputPorts_AreTwoBranches()
    {
        var def = new RunParallelAction();
        Assert.Equal(new[] { "branch1", "branch2" }, def.OutputPorts.Select(p => p.Name));
    }
}
