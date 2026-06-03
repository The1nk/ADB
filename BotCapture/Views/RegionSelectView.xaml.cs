using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class RegionSelectView : UserControl
{
    private System.Windows.Point _dragStart;
    private bool _dragging;

    public RegionSelectView()
    {
        InitializeComponent();
    }

    /// <summary>Raised with the cropped template when the user confirms a region.</summary>
    public event EventHandler<Bitmap>? RegionConfirmed;

    /// <summary>Raised when the user backs out of region selection.</summary>
    public event EventHandler? BackRequested;

    private RegionSelectionViewModel? Vm => DataContext as RegionSelectionViewModel;

    /// <summary>Call after setting DataContext to show the source image.</summary>
    public void Bind(RegionSelectionViewModel vm)
    {
        DataContext = vm;
        SourceImage.Source = BitmapInterop.ToImageSource(vm.Source);
        SelectionRect.Visibility = Visibility.Collapsed;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        _dragging = true;
        _dragStart = e.GetPosition(SourceImage);
        SourceImage.CaptureMouse();
        UpdateSelection(_dragStart, _dragStart);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        UpdateSelection(_dragStart, e.GetPosition(SourceImage));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        SourceImage.ReleaseMouseCapture();
        UpdateSelection(_dragStart, e.GetPosition(SourceImage));
    }

    private void UpdateSelection(System.Windows.Point a, System.Windows.Point b)
    {
        if (Vm is null) return;

        // Display rectangle (for the overlay), clamped to the image bounds.
        var dispLeft = Math.Max(0, Math.Min(a.X, b.X));
        var dispTop = Math.Max(0, Math.Min(a.Y, b.Y));
        var dispRight = Math.Min(SourceImage.ActualWidth, Math.Max(a.X, b.X));
        var dispBottom = Math.Min(SourceImage.ActualHeight, Math.Max(a.Y, b.Y));

        Canvas.SetLeft(SelectionRect, dispLeft);
        Canvas.SetTop(SelectionRect, dispTop);
        SelectionRect.Width = Math.Max(0, dispRight - dispLeft);
        SelectionRect.Height = Math.Max(0, dispBottom - dispTop);
        SelectionRect.Visibility = Visibility.Visible;

        // Map display coords -> source pixels by the displayed/actual ratio.
        var scaleX = Vm.Source.Width / Math.Max(1.0, SourceImage.ActualWidth);
        var scaleY = Vm.Source.Height / Math.Max(1.0, SourceImage.ActualHeight);
        Vm.Selection = new Rectangle(
            (int)Math.Round(dispLeft * scaleX),
            (int)Math.Round(dispTop * scaleY),
            (int)Math.Round((dispRight - dispLeft) * scaleX),
            (int)Math.Round((dispBottom - dispTop) * scaleY));
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        RegionConfirmed?.Invoke(this, Vm.Crop());
    }

    private void OnBack(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
