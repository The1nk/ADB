using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Serialization;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BotEditorViewModelSaveTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static string TempBotPath() => Path.Combine(Path.GetTempPath(), $"adb-save-{Guid.NewGuid():N}.bot");

    [Fact]
    public void SaveAsNew_SetsBotNameFromFileName_AndPersistsThatSave()
    {
        var editor = NewEditor();
        var path = Path.Combine(Path.GetTempPath(), $"GoToPlayerMenu-{Guid.NewGuid():N}.bot");
        try
        {
            editor.SaveAsNew(path);

            Assert.Equal(Path.GetFileNameWithoutExtension(path), editor.BotName);
            Assert.Equal(path, editor.FilePath);
            Assert.False(editor.IsDirty);

            // The name was persisted in THIS save, not just held in memory.
            var loaded = new BotSerializer().Load(path);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), loaded.Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_NoArg_RewritesExistingFile()
    {
        var editor = NewEditor();
        var path = TempBotPath();
        try
        {
            editor.SaveAsNew(path);
            editor.AddNode("control.start", 0, 0); // dirty
            Assert.True(editor.IsDirty);

            editor.Save(); // no prompt, same path

            Assert.Equal(path, editor.FilePath);
            Assert.False(editor.IsDirty);
            Assert.Single(new BotSerializer().Load(path).Actions); // the added node was written
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_NoArg_WithoutFilePath_Throws()
    {
        var editor = NewEditor();
        Assert.Throws<InvalidOperationException>(() => editor.Save());
    }

    [Fact]
    public void Save_WithPath_DoesNotChangeName()
    {
        var editor = NewEditor();
        editor.BotName = "KeepMe";
        var path = Path.Combine(Path.GetTempPath(), $"Different-{Guid.NewGuid():N}.bot");
        try
        {
            editor.Save(path); // Save As semantics — no rename
            Assert.Equal("KeepMe", editor.BotName);
            Assert.Equal(path, editor.FilePath);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExportTo_WritesFile_ButDoesNotTouchDocumentState()
    {
        var editor = NewEditor();
        editor.AddNode("control.start", 0, 0); // dirty, FilePath still null
        var path = TempBotPath();
        try
        {
            editor.ExportTo(path);

            Assert.Null(editor.FilePath);     // unchanged
            Assert.True(editor.IsDirty);      // unchanged
            Assert.True(File.Exists(path));   // but the file was written
            Assert.Single(new BotSerializer().Load(path).Actions);
        }
        finally { File.Delete(path); }
    }
}
