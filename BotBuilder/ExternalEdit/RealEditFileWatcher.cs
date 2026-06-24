using System.IO;
using BotBuilder.Core.ExternalEdit;

namespace BotBuilder.ExternalEdit;

/// <summary>Real <see cref="IEditFileWatcher"/> backed by <see cref="FileSystemWatcher"/>.</summary>
internal sealed class RealEditFileWatcher : IEditFileWatcher, IDisposable
{
    private FileSystemWatcher? _fsw;

    public event EventHandler? Changed;

    public void Start(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var fileName = Path.GetFileName(filePath);

        _fsw = new FileSystemWatcher(dir, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _fsw.Changed += OnFswChanged;
    }

    public void Stop()
    {
        if (_fsw is not null)
        {
            _fsw.EnableRaisingEvents = false;
        }
    }

    public void Dispose()
    {
        Stop();
        _fsw?.Dispose();
        _fsw = null;
    }

    private void OnFswChanged(object sender, FileSystemEventArgs e)
        => Changed?.Invoke(this, EventArgs.Empty);
}
