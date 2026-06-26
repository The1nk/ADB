namespace AdbCore.Android;

/// <summary>Binds to a connected ADB device by serial, returning an <see cref="IAndroidDevice"/> for
/// capture/automation. Lets callers (e.g. the BotCapture source picker) build a device handle without
/// depending on AdvancedSharpAdbClient directly.</summary>
public interface IAndroidDeviceConnector
{
    /// <summary>Binds the device with the given serial. Throws if it is not currently connected.</summary>
    IAndroidDevice Connect(string serial);
}
