using BotBuilder.Core.ExternalEdit;

namespace BotBuilder.Core.Tests.ExternalEdit;

public class ExternalEditSessionTests
{
    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class FakeTempFile : IEditTempFile
    {
        public Dictionary<string, string> Files { get; } = new();
        public List<string> Deleted { get; } = new();
        public string? ReadError { get; set; } // set to force TryRead to return null

        public void Write(string path, string text) => Files[path] = text;

        public string? TryRead(string path)
        {
            if (ReadError is not null) return null;
            return Files.TryGetValue(path, out var t) ? t : null;
        }

        public void TryDelete(string path)
        {
            Files.Remove(path);
            Deleted.Add(path);
        }
    }

    private sealed class FakeEditorProcess : IEditorProcess
    {
        public bool HasExited { get; private set; }
        public event EventHandler? Exited;

        public void SimulateExit()
        {
            HasExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeLauncher : IEditorProcessLauncher
    {
        public FakeEditorProcess? Launched { get; private set; }
        public string? LastExe { get; private set; }
        public string? LastArgs { get; private set; }
        public bool ShouldThrow { get; set; }

        public IEditorProcess Launch(string executable, string arguments)
        {
            if (ShouldThrow) throw new InvalidOperationException("fake launch failure");
            LastExe = executable;
            LastArgs = arguments;
            Launched = new FakeEditorProcess();
            return Launched;
        }
    }

    private sealed class FakeWatcher : IEditFileWatcher
    {
        public string? WatchedPath { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public event EventHandler? Changed;

        public void Start(string filePath) => WatchedPath = filePath;
        public void Stop() => Stopped = true;
        public void Dispose() => Disposed = true;

        public void SimulateChange() => Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------------------
    // Factory
    // ---------------------------------------------------------------------------

    private static (ExternalEditSession Session, FakeTempFile TempFile, FakeLauncher Launcher, FakeWatcher Watcher)
        Build(string command = "notepad $filename", string tempBase = @"C:\tmp\LuaEdit", string sessionId = "abc")
    {
        var tf = new FakeTempFile();
        var launcher = new FakeLauncher();
        var watcher = new FakeWatcher();
        var session = new ExternalEditSession(command, tempBase, sessionId, tf, launcher, watcher);
        return (session, tf, launcher, watcher);
    }

    // ---------------------------------------------------------------------------
    // ParseCommand tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void ParseCommand_FilenamePresent_SubstitutesQuotedPath()
    {
        var (exe, args) = ExternalEditSession.ParseCommand(@"notepad $filename", @"C:\tmp\a.lua");

        Assert.Equal("notepad", exe);
        Assert.Equal(@"""C:\tmp\a.lua""", args);
    }

    [Fact]
    public void ParseCommand_FilenameAbsent_AppendsQuotedPath()
    {
        var (exe, args) = ExternalEditSession.ParseCommand("notepad", @"C:\tmp\a.lua");

        Assert.Equal("notepad", exe);
        Assert.Equal(@"""C:\tmp\a.lua""", args);
    }

    [Fact]
    public void ParseCommand_CommandWithArgs_SplitsCorrectly()
    {
        var (exe, args) = ExternalEditSession.ParseCommand(@"code --wait $filename", @"C:\tmp\a.lua");

        Assert.Equal("code", exe);
        Assert.Equal(@"--wait ""C:\tmp\a.lua""", args);
    }

    [Fact]
    public void ParseCommand_SpacesInPath_WrapsWithQuotes()
    {
        var (exe, args) = ExternalEditSession.ParseCommand(@"notepad $filename", @"C:\my scripts\a.lua");

        Assert.Equal("notepad", exe);
        Assert.Equal(@"""C:\my scripts\a.lua""", args);
    }

    [Fact]
    public void ParseCommand_FilenameAbsent_SpaceInPath_AppendedQuoted()
    {
        var (exe, args) = ExternalEditSession.ParseCommand("notepad", @"C:\my scripts\a.lua");

        Assert.Equal("notepad", exe);
        Assert.Equal(@"""C:\my scripts\a.lua""", args);
    }

    // ---------------------------------------------------------------------------
    // TryStart tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void TryStart_EmptyCommand_ReturnsFalse()
    {
        var (session, _, _, _) = Build(command: "   ");

        var ok = session.TryStart("script", out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryStart_ValidCommand_WritesInitialTextToTempFile()
    {
        var (session, tf, _, _) = Build();

        session.TryStart("-- hello", out _);

        Assert.Equal("-- hello", tf.Files[session.TempFilePath]);
    }

    [Fact]
    public void TryStart_ValidCommand_LaunchesEditorWithCorrectExe()
    {
        var (session, _, launcher, _) = Build(command: "notepad $filename");

        session.TryStart("", out _);

        Assert.Equal("notepad", launcher.LastExe);
    }

    [Fact]
    public void TryStart_LaunchFails_ReturnsFalseAndCleansTempFile()
    {
        var (session, tf, launcher, _) = Build();
        launcher.ShouldThrow = true;

        var ok = session.TryStart("script", out var error);

        Assert.False(ok);
        Assert.Contains(session.TempFilePath, tf.Deleted);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryStart_LaunchFails_ErrorMentionsEditorCommand()
    {
        var (session, _, launcher, _) = Build();
        launcher.ShouldThrow = true;

        session.TryStart("script", out var error);

        Assert.Contains("editor command", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryStart_StartsWatcher()
    {
        var (session, _, _, watcher) = Build();

        session.TryStart("", out _);

        Assert.Equal(session.TempFilePath, watcher.WatchedPath);
    }

    // ---------------------------------------------------------------------------
    // File-changed event tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void FileChanged_RaisesTextChangedWithNewContent()
    {
        var (session, tf, _, watcher) = Build();
        session.TryStart("initial", out _);
        tf.Files[session.TempFilePath] = "updated";

        string? received = null;
        session.TextChanged += (_, t) => received = t;
        watcher.SimulateChange();

        Assert.Equal("updated", received);
    }

    [Fact]
    public void FileChanged_TwoRapidEvents_OnlyFirstRaisesTextChanged()
    {
        var (session, tf, _, watcher) = Build();
        session.TryStart("initial", out _);
        tf.Files[session.TempFilePath] = "v1";

        var count = 0;
        session.TextChanged += (_, _) => count++;
        watcher.SimulateChange();
        watcher.SimulateChange(); // within debounce window

        Assert.Equal(1, count);
    }

    // ---------------------------------------------------------------------------
    // Done / process-exit end-session tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void Done_RaisesStoppedWithFinalText()
    {
        var (session, tf, _, _) = Build();
        session.TryStart("initial", out _);
        tf.Files[session.TempFilePath] = "final text";

        ExternalEditStoppedArgs? args = null;
        session.Stopped += (_, a) => args = a;
        session.Done();

        Assert.Equal("final text", args?.FinalText);
    }

    [Fact]
    public void Done_StopsWatcher()
    {
        var (session, _, _, watcher) = Build();
        session.TryStart("", out _);

        session.Done();

        Assert.True(watcher.Stopped);
    }

    [Fact]
    public void Done_DeletesTempFile()
    {
        var (session, tf, _, _) = Build();
        session.TryStart("", out _);

        session.Done();

        Assert.Contains(session.TempFilePath, tf.Deleted);
    }

    [Fact]
    public void Done_UserRequestedFlag_IsTrue()
    {
        var (session, _, _, _) = Build();
        session.TryStart("", out _);

        ExternalEditStoppedArgs? args = null;
        session.Stopped += (_, a) => args = a;
        session.Done();

        Assert.True(args?.UserRequested);
    }

    [Fact]
    public void ProcessExit_RaisesStoppedWithFinalText()
    {
        var (session, tf, launcher, _) = Build();
        session.TryStart("initial", out _);
        tf.Files[session.TempFilePath] = "editor saved this";

        ExternalEditStoppedArgs? args = null;
        session.Stopped += (_, a) => args = a;
        launcher.Launched!.SimulateExit();

        Assert.Equal("editor saved this", args?.FinalText);
    }

    [Fact]
    public void ProcessExit_UserRequestedFlag_IsFalse()
    {
        var (session, _, launcher, _) = Build();
        session.TryStart("", out _);

        ExternalEditStoppedArgs? args = null;
        session.Stopped += (_, a) => args = a;
        launcher.Launched!.SimulateExit();

        Assert.False(args?.UserRequested);
    }

    [Fact]
    public void Done_CalledTwice_StoppedRaisedOnce()
    {
        var (session, _, _, _) = Build();
        session.TryStart("", out _);

        var count = 0;
        session.Stopped += (_, _) => count++;
        session.Done();
        session.Done();

        Assert.Equal(1, count);
    }

    [Fact]
    public void ProcessExitAndDone_StoppedRaisedOnce()
    {
        var (session, _, launcher, _) = Build();
        session.TryStart("", out _);

        var count = 0;
        session.Stopped += (_, _) => count++;
        launcher.Launched!.SimulateExit();
        session.Done(); // user also clicks Done after the editor already exited

        Assert.Equal(1, count);
    }
}
