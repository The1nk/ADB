using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class SessionView : UserControl
{
    public SessionView()
    {
        InitializeComponent();
    }

    public event EventHandler? NewCaptureRequested;
    public event EventHandler? BrowseFolderRequested;
    public event EventHandler<SessionRow>? RetestRequested;
    public event EventHandler<SessionRow>? DeleteRequested;
    public event EventHandler<SessionRow>? ReEditRequested;

    private void OnNewCapture(object sender, RoutedEventArgs e) => NewCaptureRequested?.Invoke(this, EventArgs.Empty);

    private void OnBrowse(object sender, RoutedEventArgs e) => BrowseFolderRequested?.Invoke(this, EventArgs.Empty);

    private void OnRetest(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SessionRow row)
        {
            RetestRequested?.Invoke(this, row);
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SessionRow row)
        {
            DeleteRequested?.Invoke(this, row);
        }
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // The ListBox-level double-click also bubbles from the Re-test/delete buttons; ignore those so a
        // double-click on a row action doesn't also fire a re-edit.
        if (OriginatesFromButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (RowList.SelectedItem is SessionRow row)
        {
            ReEditRequested?.Invoke(this, row);
        }
    }

    private static bool OriginatesFromButton(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is Button)
            {
                return true;
            }
        }

        return false;
    }
}
