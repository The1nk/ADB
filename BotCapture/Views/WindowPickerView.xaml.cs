using System.Windows;
using System.Windows.Controls;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class WindowPickerView : UserControl
{
    public WindowPickerView()
    {
        InitializeComponent();
    }

    private WindowPickerViewModel? Vm => DataContext as WindowPickerViewModel;

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        Vm?.Refresh();
        CapturedPreview.Source = null;
    }

    private void OnCapture(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        if (!Vm.CaptureSelected())
        {
            CapturedPreview.Source = null; // clear stale image so the error status isn't paired with an old capture
            return;
        }

        if (Vm.CapturedImage is not null)
        {
            CapturedPreview.Source = BitmapInterop.ToImageSource(Vm.CapturedImage);
        }
    }
}
