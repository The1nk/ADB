using System;
using System.IO;
using System.Windows;
using AdbUi.Theme;
using BotCapture.Core;

namespace BotCapture;

public partial class App : Application
{
    /// <summary>The app-wide theme manager. Created at startup; the MainWindow theme selector drives it.</summary>
    public ThemeManager Theme { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Theme-agnostic control styles; the active brush dictionary is merged/swapped by the manager.
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AdbUi.Theme;component/Themes/Controls.xaml", UriKind.Absolute),
        });
        Theme = new ThemeManager(
            new JsonSettingsStore(SettingsPaths.SettingsFile),
            new Win32OsThemeProbe(),
            new ResourceDictionaryThemeApplier());
        Theme.Initialize();

        string? outputPath;
        try
        {
            outputPath = CommandLineArgs.Parse(e.Args).OutputPath;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "BotCapture", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        // Resolve to an absolute path so a relative --output saves where the user expects (against the
        // working directory) rather than depending on an empty GetDirectoryName downstream.
        if (outputPath is not null)
        {
            outputPath = Path.GetFullPath(outputPath);
        }

        new MainWindow(outputPath).Show();
    }
}
