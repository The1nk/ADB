using System.Diagnostics;
using System.Threading;

namespace BotBuilder;

/// <summary>Launches BotCapture in integrated single-shot mode (<c>--output &lt;path&gt;</c>) and reports the
/// result on the captured UI <see cref="SynchronizationContext"/>: <c>true</c> when it saved (exit 0),
/// <c>false</c> on cancel/any non-zero exit.</summary>
public static class CaptureLauncher
{
    public static void Launch(string exePath, string outputPath, Action<bool> onCompleted)
    {
        var sync = SynchronizationContext.Current ?? new SynchronizationContext();

        var psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(outputPath);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            var saved = process.ExitCode == 0;
            sync.Post(_ =>
            {
                onCompleted(saved);
                process.Dispose();
            }, null);
        };

        try
        {
            process.Start();
        }
        catch (Exception)
        {
            // Couldn't launch (e.g. blocked/corrupt exe) — report no-save and don't leak the Process.
            process.Dispose();
            sync.Post(_ => onCompleted(false), null);
        }
    }
}
