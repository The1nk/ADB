namespace BotBuilder.Core.Targets;

/// <summary>Builds target selector strings from picked window/device/browser choices. Matches the formats
/// the Test-Run target picker uses (process:/title: for windows, serial: for Android, browser: for browsers).</summary>
public static class SelectorFormat
{
    public static string Window(string processName, string title)
        => string.IsNullOrEmpty(processName) ? $"title:{title}" : $"process:{processName}";

    public static string Android(string serial) => $"serial:{serial}";

    public static string Browser(string engine) => $"browser:{engine}";
}
