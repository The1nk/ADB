namespace AdbUi.Theme;

/// <summary>Resolves a <see cref="ThemeSelection"/> (which may be "follow the OS") plus the current OS theme
/// into the single <see cref="AppTheme"/> that should be applied.</summary>
public static class ThemeResolver
{
    public static AppTheme Resolve(ThemeSelection selection, AppTheme osTheme) => selection switch
    {
        ThemeSelection.Light => AppTheme.Light,
        ThemeSelection.Dark => AppTheme.Dark,
        ThemeSelection.HighContrast => AppTheme.HighContrast,
        _ => osTheme, // ThemeSelection.System
    };
}
