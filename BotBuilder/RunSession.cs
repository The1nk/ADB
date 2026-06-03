using System.Diagnostics;
using System.IO;
using System.Threading;
using BotBuilder.Core.Integration;

namespace BotBuilder;

/// <summary>Owns a running BotRunner process: pumps its stdout lines (parsed into <see cref="RunLogEntry"/>)
/// and its exit code back to the caller on the captured UI <see cref="SynchronizationContext"/>.</summary>
public sealed class RunSession
{
    private readonly Process _process;
    private readonly SynchronizationContext _sync;

    private RunSession(Process process, SynchronizationContext sync)
    {
        _process = process;
        _sync = sync;
    }

    public event EventHandler<RunLogEntry>? EntryReceived;
    public event EventHandler<int>? Exited;

    public static RunSession Start(string exePath, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var session = new RunSession(process, SynchronizationContext.Current ?? new SynchronizationContext());

        process.OutputDataReceived += (_, e) => session.OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => session.OnLine(e.Data); // runner errors arrive as plain text -> Unparsed
        process.Exited += (_, _) => session.Post(() => session.Exited?.Invoke(session, process.ExitCode));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return session;
    }

    /// <summary>Kills the run if it is still going.</summary>
    public void Stop()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) { /* already exited */ }
    }

    private void OnLine(string? line)
    {
        if (line is null)
        {
            return; // end-of-stream marker
        }

        var entry = RunnerLogParser.Parse(line);
        Post(() => EntryReceived?.Invoke(this, entry));
    }

    private void Post(Action action) => _sync.Post(_ => action(), null);
}
