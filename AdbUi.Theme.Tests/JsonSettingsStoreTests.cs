using System.IO;
using AdbUi.Theme;

namespace AdbUi.Theme.Tests;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"adb-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Load_returns_defaults_when_file_is_missing()
    {
        var store = new JsonSettingsStore(_path);

        Assert.Equal(ThemeSelection.Dark, store.Load().Theme);
    }

    [Fact]
    public void Save_then_Load_round_trips_the_theme()
    {
        var store = new JsonSettingsStore(_path);

        store.Save(new AppSettings { Theme = ThemeSelection.Dark });

        Assert.Equal(ThemeSelection.Dark, store.Load().Theme);
    }

    [Fact]
    public void Load_returns_defaults_when_file_is_corrupt()
    {
        File.WriteAllText(_path, "{ this is not valid json");
        var store = new JsonSettingsStore(_path);

        Assert.Equal(ThemeSelection.Dark, store.Load().Theme);
    }

    [Fact]
    public void Save_after_corrupt_rewrites_valid_json()
    {
        File.WriteAllText(_path, "garbage");
        var store = new JsonSettingsStore(_path);

        store.Save(new AppSettings { Theme = ThemeSelection.HighContrast });

        Assert.Equal(ThemeSelection.HighContrast, new JsonSettingsStore(_path).Load().Theme);
    }

    [Fact]
    public void Save_creates_the_parent_directory_if_missing()
    {
        var nestedDir = Path.Combine(Path.GetTempPath(), $"adb-{Guid.NewGuid():N}");
        var nestedPath = Path.Combine(nestedDir, "settings.json");
        try
        {
            new JsonSettingsStore(nestedPath).Save(new AppSettings { Theme = ThemeSelection.Light });

            Assert.True(File.Exists(nestedPath));
        }
        finally
        {
            if (Directory.Exists(nestedDir)) Directory.Delete(nestedDir, recursive: true);
        }
    }
}
