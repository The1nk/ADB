using AdbUi.Theme;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Settings;

/// <summary>Backs the Settings dialog: loads and persists application settings via <see cref="ISettingsStore"/>.
/// Theme management is intentionally left to <c>ThemeManager</c> in the WPF layer; this VM only exposes
/// the settings that live purely in the editor's config surface.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;

    [ObservableProperty]
    private string _externalEditorCommand;

    public SettingsViewModel(ISettingsStore store)
    {
        _store = store;
        var settings = store.Load();
        _externalEditorCommand = settings.ExternalEditorCommand;
    }

    /// <summary>Persists the current field values to the settings store. Theme is preserved from
    /// the previously stored value so this VM cannot accidentally clobber a theme change made
    /// elsewhere in the same session.</summary>
    public void Save()
    {
        // Round-trip the existing settings to preserve fields we don't own (e.g. Theme).
        var existing = _store.Load();
        _store.Save(existing with { ExternalEditorCommand = ExternalEditorCommand });
    }
}
