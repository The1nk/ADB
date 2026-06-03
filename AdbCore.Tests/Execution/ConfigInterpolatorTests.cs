using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ConfigInterpolatorTests
{
    private static Dictionary<string, object> Vars(params (string k, object v)[] pairs)
    {
        var d = new Dictionary<string, object>();
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    [Theory]
    [InlineData("no tokens", "no tokens")]
    [InlineData("", "")]
    public void Interpolate_NoToken_ReturnsSame(string input, string expected)
        => Assert.Equal(expected, ConfigInterpolator.Interpolate(input, Vars()));

    [Fact]
    public void Interpolate_SingleToken_Substitutes()
        => Assert.Equal("x=12", ConfigInterpolator.Interpolate("x=${a}", Vars(("a", "12"))));

    [Fact]
    public void Interpolate_MultipleTokens_AndSurroundingText()
        => Assert.Equal("(3,4)", ConfigInterpolator.Interpolate("(${x},${y})", Vars(("x", "3"), ("y", "4"))));

    [Fact]
    public void Interpolate_UnknownVariable_BecomesEmpty()
        => Assert.Equal("a=", ConfigInterpolator.Interpolate("a=${missing}", Vars()));

    [Fact]
    public void Interpolate_CoercesNonStringValue()
        => Assert.Equal("42", ConfigInterpolator.Interpolate("${n}", Vars(("n", 42))));

    [Fact]
    public void Resolve_NoToken_ReturnsSameInstance()
    {
        var action = new BotAction { TypeKey = "x", Config = { ["a"] = "plain", ["b"] = 5 } };
        Assert.Same(action, ConfigInterpolator.Resolve(action, Vars(("a", "ignored"))));
    }

    [Fact]
    public void Resolve_WithToken_ClonesAndInterpolates_OriginalUntouched()
    {
        var id = Guid.NewGuid();
        var action = new BotAction { Id = id, TypeKey = "input.click", Label = "Click", TargetId = id, Config = { ["x"] = "${cx}", ["y"] = 7 } };

        var resolved = ConfigInterpolator.Resolve(action, Vars(("cx", "120")));

        Assert.NotSame(action, resolved);
        Assert.Equal("120", resolved.Config["x"]);
        Assert.Equal(7, resolved.Config["y"]);            // non-string passes through
        Assert.Equal("${cx}", action.Config["x"]);        // original untouched
        Assert.Equal(id, resolved.Id);
        Assert.Equal("input.click", resolved.TypeKey);
        Assert.Equal("Click", resolved.Label);
        Assert.Equal(id, resolved.TargetId);
    }
}
