namespace AdbCore.Scripting;

/// <summary>The outcome of running an external process: its exit code and captured output.</summary>
public readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Runs an external process to completion. Injectable so the Lua <c>process</c> module is unit-testable.
/// A non-zero exit code is a normal <see cref="ProcessResult"/> (not an exception); failure to START the
/// process throws.</summary>
public interface IProcessRunner
{
    ProcessResult Run(string command, IReadOnlyList<string>? arguments, CancellationToken ct);
}
