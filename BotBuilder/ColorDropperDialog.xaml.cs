using System.Drawing;
using System.Windows;
using System.Windows.Media;
using AdbUi.Theme.Controls;
using BotBuilder.Core.Picker;

namespace BotBuilder;

public partial class ColorDropperDialog : Window
{
    private readonly Bitmap _frame;

    public ColorDropperDialog(Bitmap frame)
    {
        InitializeComponent();
        _frame = frame;
        Viewer.SetImage(FrameImageSource.ToImageSource(frame));
        Viewer.ImagePointerDown += OnPointerDown;
    }

    /// <summary>The sampled colour as <c>#RRGGBB</c> — valid after the dialog returns true.</summary>
    public string? PickedHex { get; private set; }

    private void OnPointerDown(object? sender, ImagePointerEventArgs e)
    {
        if (!e.InsideImage)
        {
            return; // clicked outside the image (centered margin) — ignore
        }

        var c = _frame.GetPixel(e.SourceX, e.SourceY);
        PickedHex = ColorHex.ToHex(c.R, c.G, c.B);
        Viewer.AddDot(e.SourceX, e.SourceY, Colors.White, System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
