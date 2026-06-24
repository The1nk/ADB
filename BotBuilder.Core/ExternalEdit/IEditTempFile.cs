namespace BotBuilder.Core.ExternalEdit;

/// <summary>Abstraction over temp file I/O so <see cref="ExternalEditSession"/> can be unit-tested
/// without touching the real filesystem.</summary>
public interface IEditTempFile
{
    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/>, creating parent directories
    /// as needed.</summary>
    void Write(string path, string text);

    /// <summary>Reads the text at <paramref name="path"/>. Returns <see langword="null"/> when the file
    /// does not exist or cannot be read (e.g. locked briefly after a save).</summary>
    string? TryRead(string path);

    /// <summary>Deletes <paramref name="path"/> if it exists. Errors are silently swallowed.</summary>
    void TryDelete(string path);
}
