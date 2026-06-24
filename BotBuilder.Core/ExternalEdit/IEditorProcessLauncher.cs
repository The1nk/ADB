namespace BotBuilder.Core.ExternalEdit;

/// <summary>Abstraction over process launching so <see cref="ExternalEditSession"/> can be unit-tested
/// without a real executable on disk. The implementation must use <c>UseShellExecute = true</c> so PATH
/// lookups and <c>.cmd</c> shims (e.g. <c>code.cmd</c>) resolve correctly.</summary>
public interface IEditorProcessLauncher
{
    /// <summary>Launches the given executable with the given arguments and returns a handle to it.
    /// Throws <see cref="InvalidOperationException"/> (or any exception) on failure; the session
    /// translates it into an error result.</summary>
    IEditorProcess Launch(string executable, string arguments);
}
