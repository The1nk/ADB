using System.Windows;
using AdbUi.Theme;

namespace BotBuilder;

public partial class App : Application
{
    /// <summary>The app-wide theme manager. Created at startup; the View ▸ Theme menu drives it.</summary>
    public ThemeManager Theme { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Controls.xaml + a Dark baseline are merged statically in App.xaml (anti-flash). Here we just create
        // the manager and apply the resolved theme, which swaps the Dark baseline for the saved selection.
        Theme = new ThemeManager(
            new JsonSettingsStore(SettingsPaths.SettingsFile),
            new Win32OsThemeProbe(),
            new ResourceDictionaryThemeApplier());
        Theme.Initialize();
    }
}
