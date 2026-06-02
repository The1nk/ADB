using AdbCore.Input;
using Xunit;

namespace AdbCore.Tests.Input;

public class VirtualKeysTests
{
    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("z", 0x5A)]
    [InlineData("0", 0x30)]
    [InlineData("9", 0x39)]
    [InlineData("Enter", 0x0D)]
    [InlineData("Return", 0x0D)]
    [InlineData("Esc", 0x1B)]
    [InlineData("Escape", 0x1B)]
    [InlineData("Tab", 0x09)]
    [InlineData("Space", 0x20)]
    [InlineData("Backspace", 0x08)]
    [InlineData("Delete", 0x2E)]
    [InlineData("Up", 0x26)]
    [InlineData("Down", 0x28)]
    [InlineData("Left", 0x25)]
    [InlineData("Right", 0x27)]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("home", 0x24)]
    public void TryResolve_Known_ReturnsVk(string name, int expectedVk)
    {
        Assert.True(VirtualKeys.TryResolve(name, out var vk));
        Assert.Equal((ushort)expectedVk, vk);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("F13")]
    [InlineData("NotAKey")]
    public void TryResolve_Unknown_ReturnsFalse(string name)
    {
        Assert.False(VirtualKeys.TryResolve(name, out var vk));
        Assert.Equal((ushort)0, vk);
    }
}
