using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using AdbUi.Theme.Controls;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class RegionSelectView : UserControl
{
    private bool _dragging;
    private int _startX;
    private int _startY;

    public RegionSelectView()
    {
        InitializeComponent();
        Viewer.ImagePointerDown += OnDown;
        Viewer.ImagePointerMove += OnMove;
        Viewer.ImagePointerUp += OnUp;
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
        Viewer.SetImage(BitmapInterop.ToImageSource(vm.Source));
        Viewer.ClearOverlay();
        _dragging = false;
    }

    private void OnDown(object? sender, ImagePointerEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        _dragging = true;
        _startX = e.SourceX;
        _startY = e.SourceY;
        UpdateSelection(e.SourceX, e.SourceY);
    }

    private void OnMove(object? sender, ImagePointerEventArgs e)
    {
        if (!_dragging || Vm is null)
        {
            return;
        }

        UpdateSelection(e.SourceX, e.SourceY);
    }

    private void OnUp(object? sender, ImagePointerEventArgs e)
    {
        if (!_dragging || Vm is null)
        {
            return;
        }

        _dragging = false;
        UpdateSelection(e.SourceX, e.SourceY);
    }

    private void UpdateSelection(int curX, int curY)
    {
        if (Vm is null)
        {
            return;
        }

        var x = Math.Min(_startX, curX);
        var y = Math.Min(_startY, curY);
        var w = Math.Abs(curX - _startX);
        var h = Math.Abs(curY - _startY);
        Vm.Selection = new Rectangle(x, y, w, h);
        Viewer.SetPreviewRect(x, y, w, h);
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        RegionConfirmed?.Invoke(this, Vm.Crop());
    }

    private void OnBack(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
