using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BotBuilder;

/// <summary>Converts a hex colour string (e.g. "#4A90D9") to a <see cref="SolidColorBrush"/>.</summary>
public sealed class CategoryColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value as string ?? "#9B9B9B";
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
