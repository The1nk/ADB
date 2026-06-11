using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using Xunit;

namespace AdbCore.Tests.Actions;

public class NestedBotActionTests
{
    [Fact]
    public void Metadata_MatchesContract()
    {
        var a = new NestedBotAction();
        Assert.Equal("control.nestedBot", a.TypeKey);
        Assert.Equal("Control Flow", a.Category);
        Assert.True(a.SupportsRetry);

        Assert.Single(a.InputPorts);
        Assert.Equal("in", a.InputPorts[0].Name);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, a.OutputPorts.Select(p => p.Name).ToArray());

        Assert.Equal(new[] { "sendVars", "sendTargets", "receiveVars" }, a.ConfigFields.Select(f => f.Key).ToArray());
        Assert.All(a.ConfigFields, f => Assert.Equal(ConfigFieldType.Boolean, f.Type));
    }
}
