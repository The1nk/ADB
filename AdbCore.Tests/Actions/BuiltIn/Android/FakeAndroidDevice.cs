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
    public void PressBack() => Calls.Add("back");
    public void LaunchApp(string package) => Calls.Add($"launch {package}");
    public void InstallApk(string apkPath) => Calls.Add($"install {apkPath}");
}
