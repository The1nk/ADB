using System.IO;

namespace BotBuilder.Core.ExternalEdit;

/// <summary>Manages a single "edit in external editor" session for a Lua script field.
/// <para>
/// Call <see cref="TryStart"/> with the initial script text. The session writes a <c>.lua</c> temp file,
/// launches the configured editor command, watches the file for changes, and raises
/// <see cref="TextChanged"/> each time it detects a save. Call <see cref="Done"/> or let the editor
/// process exit to end the session; both paths do a final read and raise <see cref="Stopped"/>.
/// </para>
/// </summary>
public sealed class ExternalEditSession : IDisposable
{
    private const int DebounceMs = 200;

    private readonly string _command;
    private readonly IEditTempFile _tempFile;
    private readonly IEditorProcessLauncher _launcher;
    private readonly IEditFileWatcher _watcher;
    private readonly string _tempPath;

    private IEditorProcess? _process;

    // Lock-free "run once" guard: 0 = not ended, 1 = ended.
    private int _endedFlag;

    // Debounce: suppress a second Changed event within 200 ms of the previous one.
    private DateTime _lastFiredAt = DateTime.MinValue;
    private readonly object _debounceLock = new();

    /// <summary>Raised when the watched file changes with the new text content. Fires on a background
    /// thread; callers must marshal to the UI thread before updating a bound property.</summary>
    public event EventHandler<string>? TextChanged;

    /// <summary>Raised when the session ends (either via <see cref="Done"/> or the editor process
    /// exiting). Fires on a background thread.</summary>
    public event EventHandler<ExternalEditStoppedArgs>? Stopped;

    /// <param name="command">The raw editor command string (e.g. <c>"notepad $filename"</c> or
    /// <c>"code --wait $filename"</c>). Must not be null or whitespace; validated in
    /// <see cref="TryStart"/>.</param>
    /// <param name="tempBasePath">The base directory for the temp file (e.g.
    /// <c>%TEMP%\ADB\LuaEdit</c>).</param>
    /// <param name="sessionId">A unique identifier for this session — used to construct the temp file
    /// name so concurrent sessions do not collide.</param>
    /// <param name="tempFile">File I/O abstraction.</param>
    /// <param name="launcher">Process launcher abstraction.</param>
    /// <param name="watcher">File-change watcher abstraction.</param>
    public ExternalEditSession(
        string command,
        string tempBasePath,
        string sessionId,
        IEditTempFile tempFile,
        IEditorProcessLauncher launcher,
        IEditFileWatcher watcher)
    {
        _command = command;
        _tempFile = tempFile;
        _launcher = launcher;
        _watcher = watcher;
        _tempPath = Path.Combine(tempBasePath, $"{sessionId}.lua");
    }

    /// <summary>The path of the temporary <c>.lua</c> file used for this session.</summary>
    public string TempFilePath => _tempPath;

    /// <summary>Attempts to start the session by writing the initial text, launching the editor, and
    /// beginning to watch the file. Returns <see langword="false"/> and sets
    /// <paramref name="error"/> if the command is empty or launch fails.</summary>
    public bool TryStart(string initialText, out string error)
    {
        if (string.IsNullOrWhiteSpace(_command))
        {
            error = "No editor command is configured. Open Settings and set an editor command (e.g. notepad $filename).";
            return false;
        }

        _tempFile.Write(_tempPath, initialText);

        var (exe, args) = ParseCommand(_command, _tempPath);

        try
        {
            _process = _launcher.Launch(exe, args);
            _process.Exited += OnProcessExited;
        }
        catch (Exception ex)
        {
            _tempFile.TryDelete(_tempPath);
            error = $"Could not launch editor: {ex.Message}. Check the editor command in Settings.";
            return false;
        }

        _watcher.Changed += OnFileChanged;
        _watcher.Start(_tempPath);

        error = string.Empty;
        return true;
    }

    /// <summary>Explicitly ends the session (e.g. the user clicked Done). Does a final read, stops
    /// watching, and raises <see cref="Stopped"/>. Safe to call multiple times.</summary>
    public void Done() => TryEnd(userRequested: true);

    /// <inheritdoc />
    public void Dispose()
    {
        TryEnd(userRequested: true);
        _watcher.Dispose();
    }

    // ---------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------

    private void OnProcessExited(object? sender, EventArgs e) => TryEnd(userRequested: false);

    private void OnFileChanged(object? sender, EventArgs e)
    {
        lock (_debounceLock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastFiredAt).TotalMilliseconds < DebounceMs)
            {
                return;
            }
            _lastFiredAt = now;
        }

        var text = _tempFile.TryRead(_tempPath);
        if (text is not null)
        {
            TextChanged?.Invoke(this, text);
        }
    }

    /// <summary>Thread-safe: only the first caller runs the teardown logic.</summary>
    private void TryEnd(bool userRequested)
    {
        if (Interlocked.CompareExchange(ref _endedFlag, 1, 0) != 0)
        {
            return; // already ended
        }

        _watcher.Stop();
        _watcher.Changed -= OnFileChanged;
        if (_process is not null)
        {
            _process.Exited -= OnProcessExited;
        }

        var finalText = _tempFile.TryRead(_tempPath) ?? string.Empty;
        _tempFile.TryDelete(_tempPath);

        Stopped?.Invoke(this, new ExternalEditStoppedArgs(finalText, userRequested));
    }

    /// <summary>Parses an editor command string into an executable and arguments, substituting
    /// <c>$filename</c> with a quoted version of <paramref name="tempPath"/>. If <c>$filename</c> is
    /// absent from the command, the quoted temp path is appended as a final argument.</summary>
    public static (string Exe, string Args) ParseCommand(string command, string tempPath)
    {
        var quotedPath = $"\"{tempPath}\"";
        var withFile = command.Contains("$filename", StringComparison.OrdinalIgnoreCase)
            ? command.Replace("$filename", quotedPath, StringComparison.OrdinalIgnoreCase)
            : command.TrimEnd() + " " + quotedPath;

        // Split on the first whitespace run to separate the executable from its arguments.
        var trimmed = withFile.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex < 0)
        {
            return (trimmed, string.Empty);
        }

        return (trimmed[..spaceIndex], trimmed[(spaceIndex + 1)..].TrimStart());
    }
}
