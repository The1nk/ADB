using System.IO;
using System.Windows;
using BotCapture.Core;

namespace BotCapture;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
