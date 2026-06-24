using System.Diagnostics;
using BotBuilder.Core.ExternalEdit;

namespace BotBuilder.ExternalEdit;

/// <summary>Real <see cref="IEditorProcessLauncher"/> that spawns a process via
/// <see cref="Process.Start"/>. Uses <c>UseShellExecute = true</c> so PATH lookups and
/// <c>.cmd</c> shims (e.g. <c>code.cmd</c>) resolve correctly.</summary>
internal sealed class RealEditorProcessLauncher : IEditorProcessLauncher
{
    public IEditorProcess Launch(string executable, string arguments)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = true,
        };
        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Process.Start returned null for '{executable}'.");
        return new RealEditorProcess(proc);
    }
}
