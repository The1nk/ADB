using System.IO;
using AdbUi.Theme;

namespace AdbUi.Theme.Tests.Fakes;

/// <summary>In-memory settings store. Starts from <paramref name="initial"/>; records saves.</summary>
public sealed class FakeSettingsStore : ISettingsStore
{
    private AppSettings _settings;

    public FakeSettingsStore(AppSettings? initial = null) => _settings = initial ?? new AppSettings();

    public int SaveCount { get; private set; }

    /// <summary>When true, <see cref="Save"/> throws an <see cref="IOException"/> (to simulate a locked or
    /// unwritable settings file).</summary>
    public bool ThrowOnSave { get; set; }

    public AppSettings Load() => _settings;

    public void Save(AppSettings settings)
    {
        if (ThrowOnSave) throw new IOException("Simulated settings-write failure.");
        _settings = settings;
        SaveCount++;
    }
}
