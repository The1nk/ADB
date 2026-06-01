using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class CategoryColorsTests
{
    [Fact]
    public void ColorFor_KnownCategories_ReturnsDistinctHex()
    {
        var control = CategoryColors.ColorFor("Control Flow");
        var data = CategoryColors.ColorFor("Data");

        Assert.StartsWith("#", control);
        Assert.StartsWith("#", data);
        Assert.NotEqual(control, data);
    }

    [Fact]
    public void ColorFor_IsCaseInsensitive()
    {
        Assert.Equal(CategoryColors.ColorFor("Control Flow"), CategoryColors.ColorFor("control flow"));
    }

    [Fact]
    public void ColorFor_UnknownCategory_ReturnsDefault()
    {
        Assert.Equal(CategoryColors.Default, CategoryColors.ColorFor("Nonexistent"));
    }
}
