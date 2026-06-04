using System.Collections.Generic;
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

public partial class CoordinatePickerDialog : Window
{
    private readonly CoordinatePickerViewModel _vm;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;

    public CoordinatePickerDialog(CoordinatePickerViewModel vm, Bitmap frame)
    {
        InitializeComponent();
        _vm = vm;
        _sourceWidth = frame.Width;
        _sourceHeight = frame.Height;
        FrameImage.Source = ToImageSource(frame);
        PromptText.Text = _vm.CurrentPrompt;
    }

    /// <summary>The collected (XKey, YKey, X, Y) write-back tuples — valid after the dialog returns true.</summary>
    public IReadOnlyList<(string XKey, string YKey, int X, int Y)> Results => _vm.Results();

    private void OnImageClick(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(FrameImage);
        var mapped = CoordinateMapping.ToSourcePixel(
            pos.X, pos.Y, FrameImage.ActualWidth, FrameImage.ActualHeight, _sourceWidth, _sourceHeight);
        if (mapped is not (int sx, int sy))
        {
            return; // clicked the letterbox margin — ignore
        }

        _vm.RecordClick(sx, sy);
        DrawMarker(pos);
        PromptText.Text = _vm.CurrentPrompt;

        if (_vm.IsComplete)
        {
            DialogResult = true;
            Close();
        }
    }

    private void DrawMarker(System.Windows.Point at)
    {
        var dot = new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = System.Windows.Media.Brushes.Lime,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 0, 255, 0)),
        };
        Canvas.SetLeft(dot, at.X - 7);
        Canvas.SetTop(dot, at.Y - 7);
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
