using System.Text.Json;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class BranchActionTests
{
    private static async Task<string> RunAsync(string op, string operand, string variable = "v", object? variableValue = null)
    {
        var action = new BotAction();
        action.Config[BranchAction.VariableKey] = variable;
        action.Config[BranchAction.OperatorKey] = op;
        action.Config[BranchAction.ValueKey] = operand;

        var context = new BotExecutionContext();
        if (variableValue is not null)
        {
            context.Variables[variable] = variableValue;
        }

        var result = await new BranchAction().ExecuteAsync(new ActionExecutionContext(action, context, _ => { }), default);
        Assert.True(result.Success);
        return result.OutputPort;
    }

    [Fact]
    public async Task Equals_MatchingString_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpEquals, "yes", variableValue: "yes"));

    [Fact]
    public async Task Equals_NonMatching_FollowsFalse()
        => Assert.Equal("false", await RunAsync(BranchAction.OpEquals, "yes", variableValue: "no"));

    [Fact]
    public async Task NotEquals_NonMatching_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpNotEquals, "yes", variableValue: "no"));

    [Fact]
    public async Task GreaterThan_BoxedNumber_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpGreaterThan, "3", variableValue: 5.0));

    [Fact]
    public async Task GreaterThan_FromJsonElement_FollowsFalse()
        => Assert.Equal("false", await RunAsync(BranchAction.OpGreaterThan, "9", variableValue: JsonDocument.Parse("5").RootElement));

    [Fact]
    public async Task LessThan_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpLessThan, "10", variableValue: 4.0));

    [Fact]
    public async Task GreaterThan_NonNumericVariable_FollowsFalse()
        => Assert.Equal("false", await RunAsync(BranchAction.OpGreaterThan, "3", variableValue: "abc"));

    [Fact]
    public async Task LessThan_NotLess_FollowsFalse()
        => Assert.Equal("false", await RunAsync(BranchAction.OpLessThan, "1", variableValue: 4.0));

    [Fact]
    public async Task IsTrue_BoolVariable_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpIsTrue, "", variableValue: true));

    [Fact]
    public async Task IsTrue_FalseVariable_FollowsFalse()
        => Assert.Equal("false", await RunAsync(BranchAction.OpIsTrue, "", variableValue: false));

    [Fact]
    public async Task IsFalse_BoolVariable_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpIsFalse, "", variableValue: false));

    [Fact]
    public async Task IsFalse_TrueVariable_FollowsFalse()
        => Assert.Equal("false", await RunAsync(BranchAction.OpIsFalse, "", variableValue: true));

    [Fact]
    public async Task IsEmpty_MissingVariable_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpIsEmpty, ""));

    [Fact]
    public async Task IsEmpty_PresentVariable_FollowsFalse()
        => Assert.Equal("false", await RunAsync(BranchAction.OpIsEmpty, "", variableValue: "x"));

    [Fact]
    public async Task IsNotEmpty_MissingVariable_FollowsFalse()
        => Assert.Equal("false", await RunAsync(BranchAction.OpIsNotEmpty, ""));

    [Fact]
    public async Task IsNotEmpty_PresentVariable_FollowsTrue()
        => Assert.Equal("true", await RunAsync(BranchAction.OpIsNotEmpty, "", variableValue: "x"));

    [Fact]
    public void Definition_HasTrueFalsePorts()
    {
        var def = new BranchAction();

        Assert.Equal("control.branch", def.TypeKey);
        Assert.Equal(new[] { "true", "false" }, def.OutputPorts.Select(p => p.Name));
        Assert.False(def.SupportsRetry);
    }
}
