namespace AdbCore.Android;

/// <summary>Enumerates the devices currently visible to the ADB server (for the target picker and
/// selector resolution).</summary>
public interface IAdbDevices
{
    IReadOnlyList<AdbDeviceInfo> List();
}
