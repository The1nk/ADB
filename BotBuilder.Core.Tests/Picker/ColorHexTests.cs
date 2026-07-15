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
}
