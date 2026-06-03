using AdvancedSharpAdbClient;

namespace AdbCore.Android;

/// <summary>Lists devices visible to the ADB server (starts the server if it is not already running).
/// Verified live — needs a real device.</summary>
public sealed class AdvancedSharpAdbDevices : IAdbDevices
{
    public IReadOnlyList<AdbDeviceInfo> List()
    {
        // 3.6.16: AdbServer.Instance.StartServer(string adbPath, bool restartServerIfNewer) — synchronous.
        // Passing "adb" lets the server locate adb.exe via PATH. If the server is already running
        // this is a no-op (returns AlreadyRunning) and the adbPath argument is not used.
        AdbServer.Instance.StartServer(adbPath: "adb", restartServerIfNewer: false);

        // 3.6.16: new AdbClient() + AdbClient.GetDevices() — synchronous enumeration.
        var client = new AdbClient();
        return client.GetDevices()
                     .Select(d => new AdbDeviceInfo(d.Serial, d.State.ToString()))
                     .ToList();
    }
}
