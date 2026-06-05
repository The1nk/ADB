using System;
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

        // Theme-agnostic control styles. Their colours come from the active theme brush dictionary,
        // which the ThemeManager's applier merges/swaps. Merge this once, before MainWindow loads.
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AdbUi.Theme;component/Themes/Controls.xaml", UriKind.Absolute),
        });

        Theme = new ThemeManager(
            new JsonSettingsStore(SettingsPaths.SettingsFile),
            new Win32OsThemeProbe(),
            new ResourceDictionaryThemeApplier());
        Theme.Initialize();
    }
}
