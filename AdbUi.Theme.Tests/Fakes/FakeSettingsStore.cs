using AdbUi.Theme;

namespace AdbUi.Theme.Tests.Fakes;

/// <summary>In-memory settings store. Starts from <paramref name="initial"/>; records saves.</summary>
public sealed class FakeSettingsStore : ISettingsStore
{
    private AppSettings _settings;

    public FakeSettingsStore(AppSettings? initial = null) => _settings = initial ?? new AppSettings();

    public int SaveCount { get; private set; }

    public AppSettings Load() => _settings;

    public void Save(AppSettings settings)
    {
        _settings = settings;
        SaveCount++;
    }
}
