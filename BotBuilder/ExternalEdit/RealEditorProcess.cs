using System.Diagnostics;
using BotBuilder.Core.ExternalEdit;

namespace BotBuilder.ExternalEdit;

/// <summary>Real <see cref="IEditorProcess"/> backed by a <see cref="Process"/>.</summary>
internal sealed class RealEditorProcess : IEditorProcess
{
    private readonly Process _process;

    public RealEditorProcess(Process process)
    {
        _process = process;
        // EnableRaisingEvents must be true for the Exited event to fire.
        _process.EnableRaisingEvents = true;
        _process.Exited += (s, e) => Exited?.Invoke(this, EventArgs.Empty);
    }

    public bool HasExited => _process.HasExited;
    public event EventHandler? Exited;
}
