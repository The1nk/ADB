using System.Globalization;

namespace BotBuilder.Core.Picker;

/// <summary>Formats an R/G/B triple as an uppercase <c>#RRGGBB</c> hex string (the format Measure Bar's
/// fill/empty colour fields expect). Kept dependency-free (no System.Drawing) so it lives in BotBuilder.Core.</summary>
public static class ColorHex
{
    public static string ToHex(int r, int g, int b)
        => string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
}
