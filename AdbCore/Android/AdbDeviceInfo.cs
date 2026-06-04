namespace AdbCore.Android;

/// <summary>A connected ADB device discovered by <see cref="IAdbDevices"/>.</summary>
public readonly record struct AdbDeviceInfo(string Serial, string State);
