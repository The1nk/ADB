using System.Globalization;

namespace BotBuilder.Core.Picker;

/// <summary>Formats an R/G/B triple as an uppercase <c>#RRGGBB</c> hex string (the format Measure Bar's
/// fill/empty colour fields expect). Kept dependency-free (no System.Drawing) so it lives in BotBuilder.Core.</summary>
public static class ColorHex
{
    public static string ToHex(int r, int g, int b)
        => string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");

    /// <summary>Parses a <c>#RRGGBB</c> (or <c>RRGGBB</c>) hex string into 0–255 R/G/B. Returns false for null,
    /// wrong length, or non-hex input. The inverse of <see cref="ToHex"/>.</summary>
    public static bool TryParse(string? hex, out (int R, int G, int B) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var s = hex.Trim();
        if (s.StartsWith('#'))
        {
            s = s[1..];
        }

        if (s.Length != 6
            || !int.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !int.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !int.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        rgb = (r, g, b);
        return true;
    }
}
