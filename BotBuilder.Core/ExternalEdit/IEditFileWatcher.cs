namespace BotBuilder.Core.ExternalEdit;

/// <summary>Abstraction over file-change notification so <see cref="ExternalEditSession"/> can be
/// unit-tested without a real <c>FileSystemWatcher</c>.</summary>
public interface IEditFileWatcher : IDisposable
{
    /// <summary>Raised when the watched file changes. May fire on a non-UI thread.</summary>
    event EventHandler? Changed;

    /// <summary>Begin watching the given file path.</summary>
    void Start(string filePath);

    /// <summary>Stop watching.</summary>
    void Stop();
}
