using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BotEditorViewModelTitleTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void FreshEditor_TitleIsUntitledBot()
    {
        var editor = NewEditor();
        Assert.Equal("Untitled Bot", editor.BotName);
        Assert.Equal("ADB Bot Builder: Untitled Bot", editor.WindowTitle);
    }

    [Fact]
    public void UnsavedEdit_AddsDirtyMarkerNoExtension()
    {
        var editor = NewEditor();
        editor.AddNode("control.start", 0, 0); // marks dirty, FilePath still null
        Assert.Equal("ADB Bot Builder: *Untitled Bot", editor.WindowTitle);
    }

    [Fact]
    public void Saved_TitleUsesNameWithBotExtension()
    {
        var editor = NewEditor();
        editor.BotName = "My Bot";
        var path = Path.Combine(Path.GetTempPath(), $"adb-title-{Guid.NewGuid():N}.bot");
        try
        {
            editor.Save(path);                 // sets FilePath, clears IsDirty
            Assert.Equal("ADB Bot Builder: My Bot.bot", editor.WindowTitle);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EditAfterSave_ShowsDirtyMarkerThenClearsOnResave()
    {
        var editor = NewEditor();
        editor.BotName = "My Bot";
        var path = Path.Combine(Path.GetTempPath(), $"adb-title-{Guid.NewGuid():N}.bot");
        try
        {
            editor.Save(path);
            editor.AddNode("control.start", 0, 0);
            Assert.Equal("ADB Bot Builder: *My Bot.bot", editor.WindowTitle);
            editor.Save(path);
            Assert.Equal("ADB Bot Builder: My Bot.bot", editor.WindowTitle);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WindowTitle_RaisesChangeWhenNameDirtyOrPathChanges()
    {
        var editor = NewEditor();
        var raised = 0;
        editor.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(editor.WindowTitle)) raised++; };

        editor.BotName = "X";          // -> raise
        editor.AddNode("control.start", 0, 0); // IsDirty -> raise

        Assert.True(raised >= 2);
    }
}
