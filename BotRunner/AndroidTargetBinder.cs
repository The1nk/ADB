using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdvancedSharpAdbClient;

namespace BotRunner;

/// <summary>At run start, resolves each Android target's <c>serial:</c> selector to a bound
/// <see cref="IAndroidDevice"/> handle. A missing server/device is a CLI usage error (exit 2).
/// Skips entirely when there are no Android targets in the run.</summary>
public static class AndroidTargetBinder
{
    public static void Bind(IReadOnlyDictionary<Guid, ResolvedTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        // Fast path: nothing to do if there are no Android targets.
        if (!targets.Values.Any(t => t.Type == BotTargetType.AndroidDevice))
        {
            return;
        }

        // 3.6.16: AdbServer.Instance.StartServer(string adbPath, bool restartServerIfNewer).
        // Passing "adb" lets it locate adb.exe via PATH. Already-running server is a no-op.
        AdbClient client;
        try
        {
            AdbServer.Instance.StartServer(adbPath: "adb", restartServerIfNewer: false);
            client = new AdbClient();
        }
        catch (Exception ex)
        {
            throw new CommandLineException(
                $"Could not reach the ADB server — is `adb` installed and a device connected? (see README): {ex.Message}");
        }

        // 3.6.16: AdbClient.GetDevices() — synchronous.
        var devices = client.GetDevices().ToList();

        foreach (var target in targets.Values.Where(t => t.Type == BotTargetType.AndroidDevice))
        {
            var serial = AdbSelector.ParseSerial(target.Selector)
                ?? throw new CommandLineException(
                    $"Android target selector '{target.Selector}' must be in the form 'serial:<device>'.");

            // DeviceData is a class; FirstOrDefault returns null when no matching device is found.
            var device = devices.FirstOrDefault(d => d.Serial == serial);
            if (device is null)
            {
                throw new CommandLineException(
                    $"No connected Android device with serial '{serial}'. " +
                    "Run `adb devices` to list connected devices; see README.");
            }

            target.Handle = new AdvancedSharpAdbDevice(client, device);
        }
    }
}
