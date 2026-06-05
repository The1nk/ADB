using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BotBuilder.Core.Picker;

namespace BotBuilder;

public partial class RegionPickerDialog : Window
{
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private System.Windows.Point? _dragStart;

    public RegionPickerDialog(Bitmap frame)
    {
        InitializeComponent();
        _sourceWidth = frame.Width;
        _sourceHeight = frame.Height;
        FrameImage.Source = ToImageSource(frame);
    }

    /// <summary>The chosen region in source pixels (X, Y, Width, Height); valid after the dialog returns true.</summary>
    public (int X, int Y, int Width, int Height)? Region { get; private set; }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(FrameImage);
        var origin = FrameImage.TranslatePoint(new System.Windows.Point(0, 0), OverlayCanvas);
        Canvas.SetLeft(RubberBand, origin.X + _dragStart.Value.X);
        Canvas.SetTop(RubberBand, origin.Y + _dragStart.Value.Y);
        RubberBand.Width = 0;
        RubberBand.Height = 0;
        RubberBand.Visibility = Visibility.Visible;
        FrameImage.CaptureMouse();
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not System.Windows.Point start)
        {
            return;
        }
        var p = e.GetPosition(FrameImage);
        var origin = FrameImage.TranslatePoint(new System.Windows.Point(0, 0), OverlayCanvas);
        Canvas.SetLeft(RubberBand, origin.X + System.Math.Min(start.X, p.X));
        Canvas.SetTop(RubberBand, origin.Y + System.Math.Min(start.Y, p.Y));
        RubberBand.Width = System.Math.Abs(p.X - start.X);
        RubberBand.Height = System.Math.Abs(p.Y - start.Y);
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        FrameImage.ReleaseMouseCapture();
        if (_dragStart is not System.Windows.Point start)
        {
            return;
        }
        _dragStart = null;
        RubberBand.Visibility = Visibility.Collapsed;

        var end = e.GetPosition(FrameImage);
        var a = CoordinateMapping.ToSourcePixel(start.X, start.Y, FrameImage.ActualWidth, FrameImage.ActualHeight, _sourceWidth, _sourceHeight);
        var b = CoordinateMapping.ToSourcePixel(end.X, end.Y, FrameImage.ActualWidth, FrameImage.ActualHeight, _sourceWidth, _sourceHeight);
        if (a is not (int ax, int ay) || b is not (int bx, int by))
        {
            return; // a corner fell in the letterbox margin — ignore, let the user re-drag
        }

        var region = RegionSelection.FromCorners(ax, ay, bx, by, _sourceWidth, _sourceHeight);
        if (region.Width <= 0 || region.Height <= 0)
        {
            return; // degenerate — ignore
        }

        Region = region;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static ImageSource ToImageSource(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
