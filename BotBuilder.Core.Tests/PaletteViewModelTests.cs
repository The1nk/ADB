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

    private sealed class FakeProbe : IDependencyProbe
    {
        private readonly HashSet<string> _unavailable;
        public FakeProbe(params string[] unavailable) => _unavailable = new HashSet<string>(unavailable);

        public DependencyStatus ForCategory(string category) =>
            _unavailable.Contains(category) ? new DependencyStatus(false, category + " missing") : DependencyStatus.Available;
    }

    [Fact]
    public void Unavailable_category_marks_its_items_and_leaves_others_available()
    {
        var palette = new PaletteViewModel(SeededRegistry(), new FakeProbe("Android"));

        var android = palette.Categories.Single(c => c.Name == "Android");
        Assert.False(android.IsAvailable);
        Assert.Equal("Android missing", android.DisabledReason);
        Assert.All(android.Items, i =>
        {
            Assert.False(i.IsAvailable);
            Assert.Equal("Android missing", i.DisabledReason);
        });

        var screen = palette.Categories.Single(c => c.Name == "Screen");
        Assert.True(screen.IsAvailable);
        Assert.Null(screen.DisabledReason);
        Assert.All(screen.Items, i => Assert.True(i.IsAvailable));
    }

    [Fact]
    public void Categories_GroupBuiltInsByCategory()
    {
        var palette = new PaletteViewModel(SeededRegistry());

        var control = palette.Categories.Single(c => c.Name == "Control Flow");
        var data = palette.Categories.Single(c => c.Name == "Data");

        Assert.Equal(9, control.Items.Count); // Start, End, Delay, Branch, Loop, Loop-Break, Run Parallel, Join, Nested Bot
        Assert.Equal(4, data.Items.Count); // Log, Set Variable, Comment, Math

        var input = palette.Categories.Single(c => c.Name == "Input");
        Assert.Equal(6, input.Items.Count); // Click, Right Click, Double Click, Mouse Move, Type Text, Key Press

        var android = palette.Categories.Single(c => c.Name == "Android");
        Assert.Equal(13, android.Items.Count); // Tap, Swipe, Press Back, Launch App, Install APK, Screenshot, Find Image, Wait for Image, Assert Image Absent, Read Text, Find Text, Wait for Text, Assert Text Absent

        var screen = palette.Categories.Single(c => c.Name == "Screen");
        Assert.Equal(8, screen.Items.Count); // Find/Wait/AssertAbsent Image + Screenshot + Read/Find/Wait/AssertAbsent Text

        var scripting = palette.Categories.Single(c => c.Name == "Scripting");
        Assert.Single(scripting.Items); // Run Lua Script

        var window = palette.Categories.Single(c => c.Name == "Window");
        Assert.Single(window.Items); // Activate Window
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

        Assert.Equal(47, palette.Categories.SelectMany(c => c.Items).Count()); // 9 Control Flow + 4 Data + 1 Scripting + 6 Input + 8 Screen + 13 Android + 5 Browser + 1 Window
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
