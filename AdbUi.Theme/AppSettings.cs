namespace AdbUi.Theme;

/// <summary>Persisted application settings. A general bag so future settings have a home; v1 only carries the
/// theme choice.</summary>
public sealed record AppSettings
{
    // Default to Dark (not System) so a fresh install opens dark — dark-mode users aren't flash-banged by a
    // bright window on first launch. Users can still pick System (follow OS) / Light explicitly.
    public ThemeSelection Theme { get; init; } = ThemeSelection.Dark;

    /// <summary>The command used to open a Lua script in an external editor. Use <c>$filename</c> as a
    /// placeholder for the (quoted) temp file path. Example: <c>code --wait $filename</c>.</summary>
    public string ExternalEditorCommand { get; init; } = "notepad $filename";
}
