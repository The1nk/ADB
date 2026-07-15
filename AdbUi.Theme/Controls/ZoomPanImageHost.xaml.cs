using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AdbUi.Theme.Controls;

/// <summary>A zoomable/pannable image host with an overlay. Consumers set a <see cref="BitmapSource"/> and
/// receive pointer events already mapped to source pixels; overlay marks are placed in source-pixel space
/// and reproject on zoom. Wheel zooms toward the cursor, middle-drag pans, and a toolbar offers −/%/+/Fit/100%.</summary>
public partial class ZoomPanImageHost : UserControl
{
    private readonly record struct DotMark(double SourceX, double SourceY, Color Stroke, Color Fill);

    private readonly List<DotMark> _dots = new();
    private (int X, int Y, int W, int H)? _previewRect;

    private int _sourceWidth;
    private int _sourceHeight;
    private double _scale = 1.0;
    private bool _userAdjusted;

    private bool _panning;
    private Point _panStart;
    private double _panStartH;
    private double _panStartV;

    public ZoomPanImageHost()
    {
        InitializeComponent();

        Loaded += (_, _) => { if (!_userAdjusted) Fit(); };
        Scroller.SizeChanged += (_, _) => { if (!_userAdjusted) Fit(); };

        Scroller.PreviewMouseWheel += OnPreviewMouseWheel;
        Scroller.PreviewMouseDown += OnPreviewMouseDown;
        Scroller.PreviewMouseMove += OnPreviewMouseMove;
        Scroller.PreviewMouseUp += OnPreviewMouseUp;
        Scroller.LostMouseCapture += OnScrollerLostMouseCapture;

        FrameImage.MouseLeftButtonDown += OnImageLeftDown;
        FrameImage.MouseMove += OnImageMove;
        FrameImage.MouseLeftButtonUp += OnImageLeftUp;

        KeyDown += OnKeyDown;
    }

    /// <summary>Fired on a left-button press/move/release over the image. Coordinates are source pixels
    /// (always in-bounds); <see cref="ImagePointerEventArgs.InsideImage"/> is false when the raw point was
    /// outside the image (e.g. dragged past the edge, or a click in the centered margin).</summary>
    public event EventHandler<ImagePointerEventArgs>? ImagePointerDown;

    public event EventHandler<ImagePointerEventArgs>? ImagePointerMove;

    public event EventHandler<ImagePointerEventArgs>? ImagePointerUp;

    /// <summary>Sets the frame to display and fits it to the viewport. Clears any overlay.</summary>
    public void SetImage(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        FrameImage.Source = image;
        _sourceWidth = image.PixelWidth;
        _sourceHeight = image.PixelHeight;
        _dots.Clear();
        _previewRect = null;
        _userAdjusted = false;
        ApplyScale(_scale);
        Fit();
    }

    /// <summary>Removes all overlay dots and the preview rectangle.</summary>
    public void ClearOverlay()
    {
        _dots.Clear();
        _previewRect = null;
        RedrawOverlay();
    }

    /// <summary>Adds a constant-size ring marker centered on a source pixel.</summary>
    public void AddDot(double sourceX, double sourceY, Color stroke, Color fill)
    {
        _dots.Add(new DotMark(sourceX, sourceY, stroke, fill));
        RedrawOverlay();
    }

    /// <summary>Shows a single transient selection rectangle (source coords) that scales with zoom.</summary>
    public void SetPreviewRect(int x, int y, int width, int height)
    {
        _previewRect = (x, y, width, height);
        RedrawOverlay();
    }

    /// <summary>Hides the preview rectangle.</summary>
    public void ClearPreviewRect()
    {
        _previewRect = null;
        RedrawOverlay();
    }

    /// <summary>Scales the image to fit the current viewport.</summary>
    public void Fit()
    {
        if (_sourceWidth <= 0)
        {
            return;
        }

        var vw = Scroller.ViewportWidth;
        var vh = Scroller.ViewportHeight;
        if (vw <= 0 || vh <= 0)
        {
            return;
        }

        _userAdjusted = false;
        ApplyScale(ViewportTransform.FitScale(vw, vh, _sourceWidth, _sourceHeight));
    }

    /// <summary>Sets an explicit zoom (1.0 = 100%).</summary>
    public void SetScale(double scale)
    {
        _userAdjusted = true;
        ApplyScale(scale);
    }

    private void ApplyScale(double newScale)
    {
        _scale = ViewportTransform.ClampScale(newScale);
        var w = _sourceWidth * _scale;
        var h = _sourceHeight * _scale;
        FrameImage.Width = w;
        FrameImage.Height = h;
        Overlay.Width = w;
        Overlay.Height = h;
        RenderOptions.SetBitmapScalingMode(
            FrameImage, _scale >= 1.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Linear);
        ZoomLabel.Text = $"{Math.Round(_scale * 100)}%";
        RedrawOverlay();
    }

    private void RedrawOverlay()
    {
        Overlay.Children.Clear();

        foreach (var d in _dots)
        {
            // Center the ring on the middle of the source pixel.
            var (cx, cy) = ViewportTransform.SourceToDisplay(d.SourceX + 0.5, d.SourceY + 0.5, _scale);
            var ring = new Ellipse
            {
                Width = 14,
                Height = 14,
                StrokeThickness = 2,
                Stroke = new SolidColorBrush(d.Stroke),
                Fill = new SolidColorBrush(d.Fill),
            };
            Canvas.SetLeft(ring, cx - 7);
            Canvas.SetTop(ring, cy - 7);
            Overlay.Children.Add(ring);
        }

        if (_previewRect is (int rx, int ry, int rw, int rh))
        {
            var (dx, dy) = ViewportTransform.SourceToDisplay(rx, ry, _scale);
            var box = new Rectangle
            {
                Width = rw * _scale,
                Height = rh * _scale,
                Stroke = Brushes.Lime,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xFF, 0x00)),
            };
            Canvas.SetLeft(box, dx);
            Canvas.SetTop(box, dy);
            Overlay.Children.Add(box);
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_sourceWidth <= 0)
        {
            return;
        }

        _userAdjusted = true;
        var cursor = e.GetPosition(Scroller);
        var oldScale = _scale;
        var newScale = ViewportTransform.StepScale(oldScale, e.Delta / 120);
        e.Handled = true;
        if (newScale == oldScale)
        {
            return;
        }

        ApplyScale(newScale);
        Scroller.UpdateLayout();
        Scroller.ScrollToHorizontalOffset(
            ViewportTransform.ZoomToCursorOffset(Scroller.HorizontalOffset, cursor.X, oldScale, newScale));
        Scroller.ScrollToVerticalOffset(
            ViewportTransform.ZoomToCursorOffset(Scroller.VerticalOffset, cursor.Y, oldScale, newScale));
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _panning = true;
        _panStart = e.GetPosition(Scroller);
        _panStartH = Scroller.HorizontalOffset;
        _panStartV = Scroller.VerticalOffset;
        Scroller.CaptureMouse();
        Cursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        if (e.MiddleButton != MouseButtonState.Pressed)
        {
            Scroller.ReleaseMouseCapture(); // fires LostMouseCapture -> EndPan()
            return;
        }

        var p = e.GetPosition(Scroller);
        Scroller.ScrollToHorizontalOffset(_panStartH - (p.X - _panStart.X));
        Scroller.ScrollToVerticalOffset(_panStartV - (p.Y - _panStart.Y));
        e.Handled = true;
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning || e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        Scroller.ReleaseMouseCapture();
        EndPan();
        e.Handled = true;
    }

    private void OnScrollerLostMouseCapture(object sender, MouseEventArgs e) => EndPan();

    private void EndPan()
    {
        if (!_panning)
        {
            return;
        }

        _panning = false;
        Cursor = null;
    }

    private void OnImageLeftDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        FrameImage.CaptureMouse();
        Raise(ImagePointerDown, e);
        e.Handled = true;
    }

    private void OnImageMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            Raise(ImagePointerMove, e);
        }
    }

    private void OnImageLeftUp(object sender, MouseButtonEventArgs e)
    {
        FrameImage.ReleaseMouseCapture();
        Raise(ImagePointerUp, e);
        e.Handled = true;
    }

    private void Raise(EventHandler<ImagePointerEventArgs>? handler, MouseEventArgs e)
    {
        if (handler is null)
        {
            return;
        }

        var p = e.GetPosition(FrameImage);
        var inside = ViewportTransform.PointToSource(p.X, p.Y, _scale, _sourceWidth, _sourceHeight) is not null;
        var (sx, sy) = ViewportTransform.ClampToSource(p.X, p.Y, _scale, _sourceWidth, _sourceHeight);
        handler(this, new ImagePointerEventArgs(sx, sy, inside));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.OemPlus or Key.Add:
                SetScale(ViewportTransform.StepScale(_scale, 1));
                e.Handled = true;
                break;
            case Key.OemMinus or Key.Subtract:
                SetScale(ViewportTransform.StepScale(_scale, -1));
                e.Handled = true;
                break;
            case Key.D0 or Key.NumPad0:
                Fit();
                e.Handled = true;
                break;
        }
    }

    private void OnZoomOut(object sender, RoutedEventArgs e) => SetScale(ViewportTransform.StepScale(_scale, -1));

    private void OnZoomIn(object sender, RoutedEventArgs e) => SetScale(ViewportTransform.StepScale(_scale, 1));

    private void OnFit(object sender, RoutedEventArgs e) => Fit();

    private void OnActualSize(object sender, RoutedEventArgs e) => SetScale(1.0);
}
