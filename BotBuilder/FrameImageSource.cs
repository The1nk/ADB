using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace BotBuilder;

/// <summary>Bridges a System.Drawing.Bitmap capture to a frozen WPF BitmapSource (via an in-memory PNG), so
/// callers can dispose the source Bitmap immediately. Shared by the frame-picker dialogs.</summary>
internal static class FrameImageSource
{
    public static BitmapSource ToImageSource(Bitmap bitmap)
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
