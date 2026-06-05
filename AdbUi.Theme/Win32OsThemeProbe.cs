using Microsoft.Win32;
using System.Windows;

namespace AdbUi.Theme;

/// <summary>Live OS theme probe for Windows. Reads the user's app-theme preference from the registry and the
/// high-contrast flag from WPF system parameters, and re-raises <see cref="OsThemeChanged"/> when Windows
/// signals a user-preference change. Verified live (registry + SystemEvents are environment-bound).</summary>
public sealed class Win32OsThemeProbe : IOsThemeProbe
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    public Win32OsThemeProbe()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public AppTheme Current => Detect();

    public event EventHandler? OsThemeChanged;

    private static AppTheme Detect()
    {
        // High contrast wins over light/dark.
        if (SystemParameters.HighContrast) return AppTheme.HighContrast;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // AppsUseLightTheme: 1 = light, 0 = dark. Missing => assume light.
            var value = key?.GetValue(AppsUseLightThemeValue);
            if (value is int i) return i == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch
        {
            // Registry unreadable — fall through to the light default.
        }

        return AppTheme.Light;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // General + Color + Accessibility cover dark-mode toggles and high-contrast changes.
        if (e.Category is UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.Accessibility)
        {
            OsThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
