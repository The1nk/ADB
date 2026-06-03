using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace BotCapture.Views;

/// <summary>Bridges a System.Drawing.Bitmap (capture output) to a WPF image via an in-memory PNG.</summary>
internal static class BitmapInterop
{
    public static BitmapImage ToImageSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
