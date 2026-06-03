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

    private void OnRefresh(object sender, RoutedEventArgs e) => Vm?.Refresh();

    private void OnCapture(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        if (Vm.CaptureSelected() && Vm.CapturedImage is not null)
        {
            CapturedPreview.Source = BitmapInterop.ToImageSource(Vm.CapturedImage);
        }
    }
}
