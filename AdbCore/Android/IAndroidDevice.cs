namespace AdbCore.Android;

/// <summary>Operations on one connected Android device, bound to it over the ADB server. Stored as the
/// <c>ResolvedTarget.Handle</c> for AndroidDevice targets; the Android actions call it.</summary>
public interface IAndroidDevice
{
    void Tap(int x, int y);
    void Swipe(int x1, int y1, int x2, int y2, int durationMs);

    /// <summary>Presses and holds at (x, y) for the given duration (same-point swipe).</summary>
    void LongPress(int x, int y, int durationMs);

    /// <summary>Types literal text into the focused field (via <c>input text</c>).</summary>
    void SendText(string text);

    /// <summary>Sends an Android keycode <paramref name="count"/> times (via <c>input keyevent</c>).</summary>
    void KeyEvent(int keyCode, int count);

    /// <summary>Captures the screen as PNG bytes.</summary>
    byte[] Screenshot();

    void PressBack();

    /// <summary>Launches an app by package name (its launcher activity).</summary>
    void LaunchApp(string package);

    void InstallApk(string apkPath);
}
