using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BotBuilder.Core.Picker;

namespace BotBuilder;

public partial class ColorDropperDialog : Window
{
    private readonly Bitmap _frame;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;

    public ColorDropperDialog(Bitmap frame)
    {
        InitializeComponent();
        _frame = frame;
        _sourceWidth = frame.Width;
        _sourceHeight = frame.Height;
        FrameImage.Source = ToImageSource(frame);
    }

    /// <summary>The sampled colour as <c>#RRGGBB</c> — valid after the dialog returns true.</summary>
    public string? PickedHex { get; private set; }

    private void OnImageClick(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(FrameImage);
        var mapped = CoordinateMapping.ToSourcePixel(
            pos.X, pos.Y, FrameImage.ActualWidth, FrameImage.ActualHeight, _sourceWidth, _sourceHeight);
        if (mapped is not (int sx, int sy))
        {
            return; // clicked the letterbox margin — ignore
        }

        var c = _frame.GetPixel(sx, sy);
        PickedHex = ColorHex.ToHex(c.R, c.G, c.B);
        DrawMarker(pos, c);
        DialogResult = true;
        Close();
    }

    private void DrawMarker(System.Windows.Point at, System.Drawing.Color sampled)
    {
        var dot = new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = System.Windows.Media.Brushes.White,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(sampled.R, sampled.G, sampled.B)),
        };
        // `at` is relative to FrameImage, whose Stretch=Uniform content is letterbox-centred within the host
        // Grid that MarkerCanvas fills — so translate the image's origin into the canvas before placing the dot,
        // otherwise the marker is offset by the letterbox margin (it would land in the black bars). Mirrors
        // CoordinatePickerDialog's origin translation.
        var origin = FrameImage.TranslatePoint(new System.Windows.Point(0, 0), MarkerCanvas);
        Canvas.SetLeft(dot, origin.X + at.X - 7);
        Canvas.SetTop(dot, origin.Y + at.Y - 7);
        MarkerCanvas.Children.Add(dot);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // Decodes the bitmap into a frozen WPF source so the caller can dispose the source Bitmap immediately.
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
