using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BotBuilder;

/// <summary>True -> blue selection border; false -> light grey.</summary>
public sealed class SelectionBorderConverter : IValueConverter
{
    public static readonly SelectionBorderConverter Instance = new();

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(value is true ? Color.FromRgb(0x29, 0x6F, 0xD6) : Color.FromRgb(0xDD, 0xDD, 0xDD));

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Null/empty -> Collapsed; otherwise Visible.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public static readonly NullToCollapsedConverter Instance = new();

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Shifts a 10px port ellipse so its centre sits on the anchor point.</summary>
public static class PortCenteringTransform
{
    public static readonly System.Windows.Media.TranslateTransform Instance = new(-5, -5);
}
