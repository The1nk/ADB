using System.Collections.Generic;
using AdbCore.Android;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

internal sealed class FakeAndroidDevice : IAndroidDevice
{
    public List<string> Calls { get; } = new();
    public byte[] ScreenshotBytes { get; set; } = System.Array.Empty<byte>();

    public void Tap(int x, int y) => Calls.Add($"tap {x} {y}");
    public void Swipe(int x1, int y1, int x2, int y2, int durationMs) => Calls.Add($"swipe {x1} {y1} {x2} {y2} {durationMs}");
    public byte[] Screenshot() { Calls.Add("screenshot"); return ScreenshotBytes; }
    public void LongPress(int x, int y, int durationMs) => Calls.Add($"longpress {x} {y} {durationMs}");
    public void SendText(string text) => Calls.Add($"text {text}");
    public void KeyEvent(int keyCode, int count) => Calls.Add($"keyevent {keyCode} {count}");
    public void PressBack() => Calls.Add("back");
    public void LaunchApp(string package) => Calls.Add($"launch {package}");
    public void InstallApk(string apkPath) => Calls.Add($"install {apkPath}");

    public string ActiveIme { get; set; } = "com.original/.Ime";
    public bool AdbKeyboardInstalled { get; set; } = true;

    /// <summary>When true, SetInputMethod records the call but does NOT flip ActiveIme — simulates a
    /// device whose IME switch never takes, so the Enable action polls to timeout.</summary>
    public bool SuppressImeActivation { get; set; }

    public string GetInputMethod() { Calls.Add("getime"); return ActiveIme; }
    public bool IsInputMethodAvailable(string ime) { Calls.Add($"imeavail {ime}"); return AdbKeyboardInstalled; }
    public void EnableInputMethod(string ime) => Calls.Add($"imeenable {ime}");
    public void SetInputMethod(string ime) { Calls.Add($"imeset {ime}"); if (!SuppressImeActivation) ActiveIme = ime; }
    public void SendAdbKeyboardText(string text) => Calls.Add($"adbkbtext {text}");
}
