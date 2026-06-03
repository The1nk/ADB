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

    /// <summary>Raised when the user accepts the current capture to proceed to region selection.</summary>
    public event EventHandler? CaptureAccepted;

    private void OnUseCapture(object sender, RoutedEventArgs e)
    {
        if (Vm?.HasCapture == true)
        {
            // The capture is being handed off to region select; drop the in-place preview so returning
            // to the picker doesn't show a stale screenshot (the capture is gone and the button disabled).
            CapturedPreview.Source = null;
            CaptureAccepted?.Invoke(this, EventArgs.Empty);
        }
    }
}
