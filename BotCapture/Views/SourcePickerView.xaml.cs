using System.Windows;
using System.Windows.Controls;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class SourcePickerView : UserControl
{
    public SourcePickerView()
    {
        InitializeComponent();
    }

    private SourcePickerViewModel? Vm => DataContext as SourcePickerViewModel;

    private void OnWindowsSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null)
        {
            Vm.Kind = SourceKind.Window; // setter triggers Refresh()
            CapturedPreview.Source = null;
        }
    }

    private void OnAndroidSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null)
        {
            Vm.Kind = SourceKind.Android; // setter triggers Refresh()
            CapturedPreview.Source = null;
        }
    }

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
            // The capture is handed off to region select; drop the in-place preview so returning to the
            // picker doesn't show a stale screenshot (the capture is gone and the button disabled).
            CapturedPreview.Source = null;
            CaptureAccepted?.Invoke(this, EventArgs.Empty);
        }
    }
}
