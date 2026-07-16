using BotBuilder.Core.Picker;
using Xunit;

namespace BotBuilder.Core.Tests.Picker;

public class ColorHexTests
{
    [Theory]
    [InlineData(0, 0, 0, "#000000")]
    [InlineData(255, 255, 255, "#FFFFFF")]
    [InlineData(255, 0, 128, "#FF0080")]
    [InlineData(12, 34, 56, "#0C2238")]
    public void ToHex_FormatsUppercaseRRGGBB(int r, int g, int b, string expected)
    {
        Assert.Equal(expected, ColorHex.ToHex(r, g, b));
    }

    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("FF0080", 255, 0, 128)]
    [InlineData("  #0c2238  ", 12, 34, 56)]
    public void TryParse_ParsesValidHex(string hex, int r, int g, int b)
    {
        Assert.True(ColorHex.TryParse(hex, out var rgb));
        Assert.Equal((r, g, b), rgb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("#12345")]
    [InlineData("nothex!")]
    [InlineData("#GGGGGG")]
    public void TryParse_RejectsInvalid(string? hex)
    {
        Assert.False(ColorHex.TryParse(hex, out _));
    }
}
