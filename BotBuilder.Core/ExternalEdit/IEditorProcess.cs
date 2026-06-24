namespace BotBuilder.Core.ExternalEdit;

/// <summary>Abstraction over a launched editor process so <see cref="ExternalEditSession"/> can be
/// unit-tested without spawning a real process.</summary>
public interface IEditorProcess
{
    /// <summary>True once the process has exited.</summary>
    bool HasExited { get; }

    /// <summary>Raised when the process exits. May fire on a non-UI thread.</summary>
    event EventHandler? Exited;
}
