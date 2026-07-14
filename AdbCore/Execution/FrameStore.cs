using System.Collections.Concurrent;
using AdbCore.Screen;

namespace AdbCore.Execution;

/// <summary>Per-run store of named <see cref="FrameSnapshot"/>s. A Capture Frame action writes here; readers
/// set to "Stored" source read here. Concurrent so parallel branches can read a shared frame safely.</summary>
public sealed class FrameStore
{
    private readonly ConcurrentDictionary<string, FrameSnapshot> _frames = new();

    public void Set(string name, FrameSnapshot frame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(frame);
        _frames[name] = frame;
    }

    public bool TryGet(string name, out FrameSnapshot? frame) => _frames.TryGetValue(name, out frame);

    public int Count => _frames.Count;
}
