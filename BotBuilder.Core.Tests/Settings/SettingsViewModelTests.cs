using AdbUi.Theme;
using BotBuilder.Core.Settings;

namespace BotBuilder.Core.Tests.Settings;

public class SettingsViewModelTests
{
    private sealed class FakeSettingsStore : ISettingsStore
    {
        public AppSettings Stored { get; private set; } = new();

        public AppSettings Load() => Stored;

        public void Save(AppSettings settings) => Stored = settings;
    }

    [Fact]
    public void Constructor_LoadsExternalEditorCommandFromStore()
    {
        var store = new FakeSettingsStore();
        store.Save(store.Load() with { ExternalEditorCommand = "code --wait $filename" });

        var vm = new SettingsViewModel(store);

        Assert.Equal("code --wait $filename", vm.ExternalEditorCommand);
    }

    [Fact]
    public void Constructor_DefaultCommand_WhenStoreHasDefault()
    {
        var store = new FakeSettingsStore(); // default settings

        var vm = new SettingsViewModel(store);

        Assert.Equal("notepad $filename", vm.ExternalEditorCommand);
    }

    [Fact]
    public void Save_PersistsExternalEditorCommand()
    {
        var store = new FakeSettingsStore();
        var vm = new SettingsViewModel(store);
        vm.ExternalEditorCommand = "vim $filename";

        vm.Save();

        Assert.Equal("vim $filename", store.Stored.ExternalEditorCommand);
    }

    [Fact]
    public void Save_PreservesExistingTheme()
    {
        var store = new FakeSettingsStore();
        store.Save(store.Load() with { Theme = ThemeSelection.Light });
        var vm = new SettingsViewModel(store);
        vm.ExternalEditorCommand = "vim $filename";

        vm.Save();

        Assert.Equal(ThemeSelection.Light, store.Stored.Theme);
    }

    [Fact]
    public void Save_RoundTrip_LoadAgainReturnsUpdatedCommand()
    {
        var store = new FakeSettingsStore();
        var vm = new SettingsViewModel(store);
        vm.ExternalEditorCommand = "code --wait $filename";

        vm.Save();

        var vm2 = new SettingsViewModel(store);
        Assert.Equal("code --wait $filename", vm2.ExternalEditorCommand);
    }
}
