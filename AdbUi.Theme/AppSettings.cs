namespace AdbUi.Theme;

/// <summary>Persisted application settings. A general bag so future settings have a home; v1 only carries the
/// theme choice.</summary>
public sealed record AppSettings
{
    // Default to Dark (not System) so a fresh install opens dark — dark-mode users aren't flash-banged by a
    // bright window on first launch. Users can still pick System (follow OS) / Light explicitly.
    public ThemeSelection Theme { get; init; } = ThemeSelection.Dark;
}
