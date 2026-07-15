using System.Drawing;
using System.Windows;
using AdbUi.Theme.Controls;
using BotBuilder.Core.Picker;

namespace BotBuilder;

public partial class RegionPickerDialog : Window
{
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private bool _dragging;
    private int _startX;
    private int _startY;

    public RegionPickerDialog(Bitmap frame)
    {
        InitializeComponent();
        _sourceWidth = frame.Width;
        _sourceHeight = frame.Height;
        Viewer.SetImage(FrameImageSource.ToImageSource(frame));
        Viewer.ImagePointerDown += OnDown;
        Viewer.ImagePointerMove += OnMove;
        Viewer.ImagePointerUp += OnUp;
    }

    /// <summary>The chosen region in source pixels (X, Y, Width, Height); valid after the dialog returns true.</summary>
    public (int X, int Y, int Width, int Height)? Region { get; private set; }

    private void OnDown(object? sender, ImagePointerEventArgs e)
    {
        _dragging = true;
        _startX = e.SourceX;
        _startY = e.SourceY;
        Viewer.SetPreviewRect(e.SourceX, e.SourceY, 0, 0);
    }

    private void OnMove(object? sender, ImagePointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var r = RegionSelection.FromCorners(_startX, _startY, e.SourceX, e.SourceY, _sourceWidth, _sourceHeight);
        Viewer.SetPreviewRect(r.X, r.Y, r.Width, r.Height);
    }

    private void OnUp(object? sender, ImagePointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        var r = RegionSelection.FromCorners(_startX, _startY, e.SourceX, e.SourceY, _sourceWidth, _sourceHeight);
        if (r.Width <= 0 || r.Height <= 0)
        {
            Viewer.ClearPreviewRect();
            return; // degenerate — let the user re-drag
        }

        Region = r;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
