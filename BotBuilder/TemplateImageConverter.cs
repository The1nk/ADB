using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace BotBuilder;

/// <summary>Produces the template preview image, preferring embedded base64 bytes (values[0]) over the source
/// file path (values[1]); returns null when neither yields a decodable image. Cached on load so files aren't
/// locked.</summary>
public sealed class TemplateImageConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var base64 = values is { Length: > 0 } ? values[0] as string : null;
        if (!string.IsNullOrWhiteSpace(base64))
        {
            try { return Decode(System.Convert.FromBase64String(base64)); }
            catch { /* fall through to the path */ }
        }

        var path = values is { Length: > 1 } ? values[1] as string : null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { return Decode(File.ReadAllBytes(path)); }
            catch { return null; }
        }

        return null;
    }

    private static BitmapImage Decode(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
