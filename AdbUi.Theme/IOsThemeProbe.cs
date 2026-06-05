namespace AdbUi.Theme;

/// <summary>Reports the current OS theme and raises an event when the user changes it. Used only while the
/// app's theme selection is <see cref="ThemeSelection.System"/>.</summary>
public interface IOsThemeProbe
{
    /// <summary>The OS's current effective theme.</summary>
    AppTheme Current { get; }

    /// <summary>Raised when the OS theme changes (e.g. the user toggles Windows dark mode).</summary>
    event EventHandler? OsThemeChanged;
}
