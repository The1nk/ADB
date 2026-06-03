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

        new MainWindow(outputPath).Show();
    }
}
