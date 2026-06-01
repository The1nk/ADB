namespace AdbCore.Models;

/// <summary>The kind of automation target a <see cref="BotTarget"/> represents.</summary>
public enum BotTargetType
{
    /// <summary>Win32 HWND / FlaUI — desktop apps or emulators.</summary>
    Window,

    /// <summary>ADB device.</summary>
    AndroidDevice,

    /// <summary>Playwright browser context.</summary>
    Browser,
}
