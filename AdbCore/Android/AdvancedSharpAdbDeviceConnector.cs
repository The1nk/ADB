using AdvancedSharpAdbClient;

namespace AdbCore.Android;

/// <summary>Live <see cref="IAndroidDeviceConnector"/>: starts the ADB server (locating adb via PATH),
/// resolves the device by serial, and wraps it in an <see cref="AdvancedSharpAdbDevice"/>. Verified live —
/// needs a real device.</summary>
public sealed class AdvancedSharpAdbDeviceConnector : IAndroidDeviceConnector
{
    public IAndroidDevice Connect(string serial)
    {
        AdbServer.Instance.StartServer(adbPath: "adb", restartServerIfNewer: false);
        var client = new AdbClient();
        var device = client.GetDevices().FirstOrDefault(d => d.Serial == serial)
            ?? throw new InvalidOperationException($"ADB device '{serial}' is not currently connected.");
        return new AdvancedSharpAdbDevice(client, device);
    }
}
