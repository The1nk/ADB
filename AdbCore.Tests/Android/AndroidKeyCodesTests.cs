using AdbCore.Android;
using Xunit;

namespace AdbCore.Tests.Android;

public class AndroidKeyCodesTests
{
    [Theory]
    [InlineData("Backspace", 67)]
    [InlineData("Delete (Fwd)", 112)]
    [InlineData("Enter", 66)]
    [InlineData("Tab", 61)]
    [InlineData("Space", 62)]
    [InlineData("Up", 19)]
    [InlineData("Down", 20)]
    [InlineData("Left", 21)]
    [InlineData("Right", 22)]
    [InlineData("Home", 122)]
    [InlineData("End", 123)]
    [InlineData("Escape", 111)]
    public void TryResolve_KnownName_ReturnsCode(string name, int expected)
    {
        Assert.True(AndroidKeyCodes.TryResolve(name, out var code));
        Assert.Equal(expected, code);
    }

    [Fact]
    public void TryResolve_IsCaseInsensitive()
    {
        Assert.True(AndroidKeyCodes.TryResolve("backSPACE", out var code));
        Assert.Equal(67, code);
    }

    [Fact]
    public void TryResolve_UnknownOrEmpty_ReturnsFalse()
    {
        Assert.False(AndroidKeyCodes.TryResolve("Meta", out _));
        Assert.False(AndroidKeyCodes.TryResolve("", out _));
        Assert.False(AndroidKeyCodes.TryResolve("   ", out _));
    }

    [Fact]
    public void Names_AreOrdered_AndEveryNameResolves()
    {
        Assert.Equal("Backspace", AndroidKeyCodes.Names[0]);
        Assert.Equal(12, AndroidKeyCodes.Names.Count);
        Assert.All(AndroidKeyCodes.Names, n => Assert.True(AndroidKeyCodes.TryResolve(n, out _)));
    }
}
