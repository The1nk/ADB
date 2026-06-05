using System.IO;

namespace AdbUi.Theme;

/// <summary>Resolves the on-disk location of the shared settings file: <c>%AppData%/ADB/settings.json</c>.
/// Both BotBuilder and BotCapture read/write this same file so they stay in sync on the chosen theme.</summary>
public static class SettingsPaths
{
    public static string SettingsFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADB",
            "settings.json");
}
