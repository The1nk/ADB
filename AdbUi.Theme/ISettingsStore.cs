namespace AdbUi.Theme;

/// <summary>Loads and saves <see cref="AppSettings"/>. Implementations must never throw on a missing or corrupt
/// store — they return defaults instead.</summary>
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
