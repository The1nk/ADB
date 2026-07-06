using System.IO;
using AdbUi.Theme;
using Xunit;

namespace AdbUi.Theme.Tests;

public class AppSettingsCollapseTests
{
    [Fact]
    public void RoundTrips_PanelCollapseFlags_AndPreservesOtherFields()
    {
        var path = Path.Combine(Path.GetTempPath(), "adb-settings-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            var loaded = store.Load();
            store.Save(loaded with { ToolboxCollapsed = true, PropertiesCollapsed = true });

            var again = new JsonSettingsStore(path).Load();
            Assert.True(again.ToolboxCollapsed);
            Assert.True(again.PropertiesCollapsed);
            Assert.Equal(loaded.Theme, again.Theme);                       // untouched field preserved
            Assert.Equal(loaded.ExternalEditorCommand, again.ExternalEditorCommand);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Defaults_AreFalse()
    {
        var s = new AppSettings();
        Assert.False(s.ToolboxCollapsed);
        Assert.False(s.PropertiesCollapsed);
    }
}
