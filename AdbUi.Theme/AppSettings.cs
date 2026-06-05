namespace AdbUi.Theme;

/// <summary>Persisted application settings. A general bag so future settings have a home; v1 only carries the
/// theme choice.</summary>
public sealed record AppSettings
{
    public ThemeSelection Theme { get; init; } = ThemeSelection.System;
}
