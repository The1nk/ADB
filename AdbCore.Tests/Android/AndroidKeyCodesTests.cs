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
    [InlineData("Paste", 279)]
    [InlineData("Copy", 278)]
    [InlineData("Cut", 277)]
    [InlineData("Home Button", 3)]
    [InlineData("Back", 4)]
    [InlineData("Recent Apps", 187)]
    [InlineData("Menu", 82)]
    [InlineData("Search", 84)]
    [InlineData("Page Up", 92)]
    [InlineData("Page Down", 93)]
    [InlineData("Power", 26)]
    [InlineData("Wake", 224)]
    [InlineData("Sleep", 223)]
    [InlineData("Volume Up", 24)]
    [InlineData("Volume Down", 25)]
    [InlineData("Mute", 164)]
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
        Assert.Equal(28, AndroidKeyCodes.Names.Count);
        Assert.Contains("Paste", AndroidKeyCodes.Names);
        Assert.All(AndroidKeyCodes.Names, n => Assert.True(AndroidKeyCodes.TryResolve(n, out _)));
    }
}
