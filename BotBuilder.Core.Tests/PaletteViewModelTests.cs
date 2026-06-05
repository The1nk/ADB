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

        Assert.Equal(7, control.Items.Count); // Start, End, Delay, Branch, Loop, Run Parallel, Join
        Assert.Equal(3, data.Items.Count); // Log, Set Variable, Comment

        var input = palette.Categories.Single(c => c.Name == "Input");
        Assert.Equal(6, input.Items.Count); // Click, Right Click, Double Click, Mouse Move, Type Text, Key Press

        var android = palette.Categories.Single(c => c.Name == "Android");
        Assert.Equal(13, android.Items.Count); // Tap, Swipe, Press Back, Launch App, Install APK, Screenshot, Find Image, Wait for Image, Assert Image Absent, Read Text, Find Text, Wait for Text, Assert Text Absent

        var screen = palette.Categories.Single(c => c.Name == "Screen");
        Assert.Equal(8, screen.Items.Count); // Find/Wait/AssertAbsent Image + Screenshot + Read/Find/Wait/AssertAbsent Text

        var scripting = palette.Categories.Single(c => c.Name == "Scripting");
        Assert.Equal(1, scripting.Items.Count); // Run Lua Script
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

        Assert.Equal(43, palette.Categories.SelectMany(c => c.Items).Count()); // 7 Control Flow + 3 Data + 1 Scripting + 6 Input + 8 Screen + 13 Android + 5 Browser
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
