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

    /// <summary>Reads the currently-active IME id (secure setting <c>default_input_method</c>).</summary>
    string GetInputMethod();

    /// <summary>True if <paramref name="ime"/> appears in the device's installed IME list.</summary>
    bool IsInputMethodAvailable(string ime);

    /// <summary>Enables <paramref name="ime"/> so it can be set active (<c>ime enable</c>).</summary>
    void EnableInputMethod(string ime);

    /// <summary>Makes <paramref name="ime"/> the active input method (<c>ime set</c>).</summary>
    void SetInputMethod(string ime);

    /// <summary>Types Unicode text via the active ADBKeyboard IME (base64 <c>ADB_INPUT_B64</c> broadcast).</summary>
    void SendAdbKeyboardText(string text);

    /// <summary>Captures the screen as PNG bytes.</summary>
    byte[] Screenshot();

    void PressBack();

    /// <summary>Launches an app by package name (its launcher activity).</summary>
    void LaunchApp(string package);

    void InstallApk(string apkPath);
}
