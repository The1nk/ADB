using System.Diagnostics;

namespace AdbCore.Scripting;

/// <summary>The real <see cref="IProcessRunner"/> backed by <see cref="System.Diagnostics.Process"/>. Runs the
/// process to completion (honoring the token, killing the process tree if cancelled) and captures stdout/stderr.</summary>
public sealed class LiveProcessRunner : IProcessRunner
{
    public ProcessResult Run(string command, IReadOnlyList<string>? arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (arguments is not null)
            foreach (var a in arguments)
                psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start process '{command}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        try
        {
            process.WaitForExitAsync(ct).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        return new ProcessResult(process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }
}
