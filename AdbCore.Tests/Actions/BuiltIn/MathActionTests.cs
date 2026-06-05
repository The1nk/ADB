using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class MathActionTests
{
    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static (ActionResult result, BotExecutionContext ctx) Run(string op, string left, string right, string resultVar = "r")
    {
        var ctx = new BotExecutionContext();
        var action = new BotAction
        {
            Config =
            {
                [MathAction.OperationKey] = op,
                [MathAction.LeftKey] = left,
                [MathAction.RightKey] = right,
                [MathAction.ResultKey] = resultVar,
            },
        };
        var r = new MathAction().ExecuteAsync(Exec(action, ctx), default).GetAwaiter().GetResult();
        return (r, ctx);
    }

    [Theory]
    [InlineData(MathAction.OpAdd, "2", "3", 5d)]
    [InlineData(MathAction.OpSubtract, "10", "4", 6d)]
    [InlineData(MathAction.OpMultiply, "6", "7", 42d)]
    [InlineData(MathAction.OpDivide, "9", "2", 4.5d)]
    [InlineData(MathAction.OpModulo, "7", "3", 1d)]
    [InlineData(MathAction.OpPower, "2", "10", 1024d)]
    [InlineData(MathAction.OpMin, "3", "7", 3d)]
    [InlineData(MathAction.OpMax, "3", "7", 7d)]
    public void Binary_Ops_Compute(string op, string l, string r, double expected)
    {
        var (res, ctx) = Run(op, l, r);
        Assert.True(res.Success);
        Assert.Equal("onSuccess", res.OutputPort);
        Assert.Equal(expected, (double)ctx.Variables["r"]);
    }

    [Theory]
    [InlineData(MathAction.OpFloor, "2.9", 2d)]
    [InlineData(MathAction.OpCeil, "2.1", 3d)]
    [InlineData(MathAction.OpRound, "2.5", 3d)]
    [InlineData(MathAction.OpAbs, "-4", 4d)]
    [InlineData(MathAction.OpSqrt, "9", 3d)]
    [InlineData(MathAction.OpNegate, "5", -5d)]
    public void Unary_Ops_Compute(string op, string l, double expected)
    {
        var (res, ctx) = Run(op, l, "");
        Assert.True(res.Success);
        Assert.Equal(expected, (double)ctx.Variables["r"]);
    }

    [Fact]
    public void Random_InUnitInterval()
    {
        for (var i = 0; i < 50; i++)
        {
            var (res, ctx) = Run(MathAction.OpRandom, "", "");
            Assert.True(res.Success);
            var v = (double)ctx.Variables["r"];
            Assert.InRange(v, 0d, 1d);
            Assert.True(v < 1d);
        }
    }

    [Fact]
    public void RandomInt_InInclusiveRange_AndIntegral()
    {
        for (var i = 0; i < 100; i++)
        {
            var (res, ctx) = Run(MathAction.OpRandomInt, "1", "6");
            Assert.True(res.Success);
            var v = (double)ctx.Variables["r"];
            Assert.InRange(v, 1d, 6d);
            Assert.Equal(v, System.Math.Floor(v));
        }
    }

    [Fact]
    public void RandomInt_SingleValue()
    {
        var (res, ctx) = Run(MathAction.OpRandomInt, "4", "4");
        Assert.True(res.Success);
        Assert.Equal(4d, (double)ctx.Variables["r"]);
    }

    [Fact]
    public void DivideByZero_Fails()
    {
        var (res, _) = Run(MathAction.OpDivide, "5", "0");
        Assert.False(res.Success);
        Assert.Contains("divide by zero", res.ErrorMessage);
    }

    [Fact]
    public void ModuloByZero_Fails()
    {
        var (res, _) = Run(MathAction.OpModulo, "5", "0");
        Assert.False(res.Success);
        Assert.Contains("modulo by zero", res.ErrorMessage);
    }

    [Fact]
    public void SqrtOfNegative_Fails_NonFinite()
    {
        var (res, _) = Run(MathAction.OpSqrt, "-1", "");
        Assert.False(res.Success);
        Assert.Contains("not a finite number", res.ErrorMessage);
    }

    [Fact]
    public void RandomInt_MinGreaterThanMax_Fails()
    {
        var (res, _) = Run(MathAction.OpRandomInt, "10", "2");
        Assert.False(res.Success);
        Assert.Contains("min", res.ErrorMessage);
    }

    [Fact]
    public void NonNumericOperand_Fails()
    {
        var (res, _) = Run(MathAction.OpAdd, "abc", "3");
        Assert.False(res.Success);
        Assert.Contains("not a number", res.ErrorMessage);
    }

    [Fact]
    public void NonNumericRightOperand_Fails()
    {
        var (res, _) = Run(MathAction.OpAdd, "3", "abc");
        Assert.False(res.Success);
        Assert.Contains("not a number", res.ErrorMessage);
    }

    [Fact]
    public void EmptyResultVariable_Fails()
    {
        var (res, _) = Run(MathAction.OpAdd, "1", "2", resultVar: "");
        Assert.False(res.Success);
        Assert.Contains("result variable", res.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new MathAction();
        Assert.Equal("data.math", def.TypeKey);
        Assert.Equal("Math", def.DisplayName);
        Assert.Equal("Data", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        var opField = def.ConfigFields.Single(f => f.Key == MathAction.OperationKey);
        Assert.Equal(ConfigFieldType.Enum, opField.Type);
        Assert.Equal(16, opField.Options!.Count);
        Assert.Contains(def.ConfigFields, f => f.Key == MathAction.LeftKey);
        Assert.Contains(def.ConfigFields, f => f.Key == MathAction.RightKey);
        Assert.Contains(def.ConfigFields, f => f.Key == MathAction.ResultKey);
    }
}
