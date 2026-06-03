using System;
using System.Globalization;
using System.Windows.Data;

namespace BotCapture.Views;

/// <summary>Maps a re-test result (bool?) to a status glyph: null = "—", true = "🟢", false = "🔴".</summary>
public sealed class RetestIndicatorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool matched ? (matched ? "🟢" : "🔴") : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
