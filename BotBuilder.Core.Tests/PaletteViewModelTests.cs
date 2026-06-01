using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core.Palette;
using Xunit;

namespace BotBuilder.Core.Tests;

public class PaletteViewModelTests
{
    private static ActionRegistry SeededRegistry()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return defs;
    }

    [Fact]
    public void Categories_GroupBuiltInsByCategory()
    {
        var palette = new PaletteViewModel(SeededRegistry());

        var control = palette.Categories.Single(c => c.Name == "Control Flow");
        var data = palette.Categories.Single(c => c.Name == "Data");

        Assert.Equal(2, control.Items.Count);
        Assert.Single(data.Items);
    }

    [Fact]
    public void Search_FiltersByDisplayName_CaseInsensitive()
    {
        var palette = new PaletteViewModel(SeededRegistry()) { SearchText = "LOG" };

        var allItems = palette.Categories.SelectMany(c => c.Items).ToList();

        Assert.Single(allItems);
        Assert.Equal("data.log", allItems[0].TypeKey);
    }

    [Fact]
    public void Search_DropsEmptyCategories()
    {
        var palette = new PaletteViewModel(SeededRegistry()) { SearchText = "log" };

        Assert.DoesNotContain(palette.Categories, c => c.Name == "Control Flow");
    }

    [Fact]
    public void ClearingSearch_RestoresAll()
    {
        var palette = new PaletteViewModel(SeededRegistry()) { SearchText = "log" };
        palette.SearchText = "";

        Assert.Equal(3, palette.Categories.SelectMany(c => c.Items).Count());
    }

    [Fact]
    public void Search_MatchesByCategoryName()
    {
        var palette = new PaletteViewModel(SeededRegistry()) { SearchText = "control" };

        var typeKeys = palette.Categories.SelectMany(c => c.Items).Select(i => i.TypeKey).ToList();

        // "control" matches the "Control Flow" category -> Start + End, but not data.log
        Assert.Contains("control.start", typeKeys);
        Assert.Contains("control.end", typeKeys);
        Assert.DoesNotContain("data.log", typeKeys);
    }
}
