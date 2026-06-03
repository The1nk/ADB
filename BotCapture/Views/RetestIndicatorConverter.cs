using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BotCapture.Views;

/// <summary>Maps a re-test result (bool?) to the status dot's fill brush: null = grey (not tested yet),
/// true = green (still matches), false = red (no match). A coloured <c>Ellipse</c> is used instead of a
/// 🟢/🔴 emoji because WPF's text rendering ignores colour-emoji glyphs (they come out monochrome).</summary>
public sealed class RetestIndicatorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool matched ? (matched ? Brushes.LimeGreen : Brushes.Red) : Brushes.LightGray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
