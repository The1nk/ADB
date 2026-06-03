namespace AdbCore.Android;

/// <summary>Operations on one connected Android device, bound to it over the ADB server. Stored as the
/// <c>ResolvedTarget.Handle</c> for AndroidDevice targets; the Android actions call it.</summary>
public interface IAndroidDevice
{
    void Tap(int x, int y);
    void Swipe(int x1, int y1, int x2, int y2, int durationMs);

    /// <summary>Captures the screen as PNG bytes.</summary>
    byte[] Screenshot();

    void PressBack();

    /// <summary>Launches an app by package name (its launcher activity).</summary>
    void LaunchApp(string package);

    void InstallApk(string apkPath);
}
