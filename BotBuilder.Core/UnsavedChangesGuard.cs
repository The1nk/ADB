namespace BotBuilder.Core;

/// <summary>The user's answer to "save changes before continuing?".</summary>
public enum SaveChoice { Save, DontSave, Cancel }

/// <summary>Decides whether a New/Open/close should proceed when the document may have unsaved changes.
/// Pure orchestration so it is unit-testable without WPF: the caller supplies the dirty check, the prompt,
/// and the save action (which returns whether the save actually completed).</summary>
public static class UnsavedChangesGuard
{
    /// <summary>Returns true to proceed, false to abort. Prompts only when dirty; on Save, proceeds only if
    /// <paramref name="save"/> returns true (e.g. the user didn't cancel the file dialog).</summary>
    public static bool ConfirmProceed(System.Func<bool> isDirty, System.Func<SaveChoice> ask, System.Func<bool> save)
    {
        if (!isDirty()) return true;
        return ask() switch
        {
            SaveChoice.Save => save(),
            SaveChoice.DontSave => true,
            _ => false,
        };
    }
}
