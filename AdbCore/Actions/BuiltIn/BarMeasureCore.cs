using System.Drawing;
using System.Globalization;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>The scan direction for a bar: which edge is 0% and which axis to walk.</summary>
public enum BarDirection { LeftToRight, RightToLeft, TopToBottom, BottomToTop }

/// <summary>The result of measuring a bar: the mapped integer value and the raw filled fraction (0..1).</summary>
public readonly record struct BarResult(int Value, double Fraction);

/// <summary>Capture-source-independent core for the Measure Bar actions: reads a solid-fill bar's value from a
/// <see cref="FrameSnapshot"/> by scanning the ROI centerline and mapping the leading filled fraction onto
/// [Min, Max]. Classification: nearest-color when both Fill and Empty are set; within-tolerance of Fill when
/// only Fill is set; NOT-within-tolerance of Empty when only Empty is set.</summary>
public static class BarMeasureCore
{
    public const string FillColorKey = "fillColor";
    public const string EmptyColorKey = "emptyColor";
    public const string ToleranceKey = "tolerance";
    public const string DirectionKey = "direction";
    public const string MinValueKey = "minValue";
    public const string MaxValueKey = "maxValue";
    public const string ResultVarKey = "resultVar";
    public const int DefaultTolerance = 30;
    public const int DefaultMin = 0;
    public const int DefaultMax = 15;
    public const string DefaultResultVar = "bar";

    /// <summary>The Measure-Bar-specific config fields (colors, tolerance, direction, min/max, result var).
    /// The action appends the shared ROI + Source fields around these.</summary>
    public static IEnumerable<ConfigField> Fields() =>
    [
        new ConfigField { Key = FillColorKey, Label = "Fill Color (hex)", Type = ConfigFieldType.Color },
        new ConfigField { Key = EmptyColorKey, Label = "Empty Color (hex)", Type = ConfigFieldType.Color },
        new ConfigField { Key = ToleranceKey, Label = "Tolerance", Type = ConfigFieldType.Number, DefaultValue = DefaultTolerance },
        new ConfigField
        {
            Key = DirectionKey, Label = "Direction", Type = ConfigFieldType.Enum,
            DefaultValue = nameof(BarDirection.LeftToRight),
            Options = new()
            {
                nameof(BarDirection.LeftToRight), nameof(BarDirection.RightToLeft),
                nameof(BarDirection.TopToBottom), nameof(BarDirection.BottomToTop),
            },
        },
        new ConfigField { Key = MinValueKey, Label = "Min Value", Type = ConfigFieldType.Number, DefaultValue = DefaultMin },
        new ConfigField { Key = MaxValueKey, Label = "Max Value", Type = ConfigFieldType.Number, DefaultValue = DefaultMax },
        new ConfigField { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar },
    ];

    /// <summary>Parses "#RRGGBB" or "RRGGBB" to a <see cref="Color"/>; null for blank/invalid.</summary>
    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) { return null; }
        var s = hex.Trim();
        if (s.StartsWith('#')) { s = s[1..]; }
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) { return null; }
        return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    public static BarResult Measure(FrameSnapshot frame, IReadOnlyDictionary<string, object> config)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var fill = ParseColor(ConfigValues.GetString(config, FillColorKey));
        var empty = ParseColor(ConfigValues.GetString(config, EmptyColorKey));
        if (fill is null && empty is null)
        {
            throw new ArgumentException("Measure Bar requires a Fill Color or an Empty Color (at least one).");
        }

        if (fill is Color fc && empty is Color ec && fc.ToArgb() == ec.ToArgb())
        {
            throw new ArgumentException("Measure Bar Fill Color and Empty Color must differ.");
        }

        var roi = TemplateMatchCore.ResolveRegion(config, frame.Width, frame.Height)
            ?? throw new ArgumentException("Measure Bar requires a Region (ROI) with positive width and height.");

        var tolerance = ConfigValues.GetInt(config, ToleranceKey, DefaultTolerance);
        var min = ConfigValues.GetInt(config, MinValueKey, DefaultMin);
        var max = ConfigValues.GetInt(config, MaxValueKey, DefaultMax);
        var direction = ParseDirection(ConfigValues.GetString(config, DirectionKey, nameof(BarDirection.LeftToRight)));

        var (axisLength, run) = ScanRun(frame, roi, direction, fill, empty, tolerance);
        var fraction = axisLength > 0 ? (double)run / axisLength : 0d;
        var value = (int)Math.Round(min + fraction * (max - min), MidpointRounding.AwayFromZero);
        value = Math.Clamp(value, Math.Min(min, max), Math.Max(min, max));
        return new BarResult(value, fraction);
    }

    /// <summary>Writes the value (under <paramref name="prefix"/>) and the fraction (under
    /// <c>&lt;prefix&gt;Fraction</c>) as InvariantCulture strings.</summary>
    public static void WriteResult(IDictionary<string, object> variables, string prefix, BarResult result)
    {
        variables[prefix] = result.Value.ToString(CultureInfo.InvariantCulture);
        variables[$"{prefix}Fraction"] = result.Fraction.ToString(CultureInfo.InvariantCulture);
    }

    private static BarDirection ParseDirection(string value)
        => Enum.TryParse<BarDirection>(value, ignoreCase: true, out var d) ? d : BarDirection.LeftToRight;

    private static (int AxisLength, int Run) ScanRun(FrameSnapshot frame, Rectangle roi, BarDirection dir, Color? fill, Color? empty, int tolerance)
    {
        var run = 0;
        switch (dir)
        {
            case BarDirection.LeftToRight:
            {
                var y = roi.Y + roi.Height / 2;
                for (var x = roi.Left; x < roi.Right; x++) { if (IsFilled(frame.GetPixel(x, y), fill, empty, tolerance)) { run++; } else { break; } }
                return (roi.Width, run);
            }
            case BarDirection.RightToLeft:
            {
                var y = roi.Y + roi.Height / 2;
                for (var x = roi.Right - 1; x >= roi.Left; x--) { if (IsFilled(frame.GetPixel(x, y), fill, empty, tolerance)) { run++; } else { break; } }
                return (roi.Width, run);
            }
            case BarDirection.TopToBottom:
            {
                var x = roi.X + roi.Width / 2;
                for (var y = roi.Top; y < roi.Bottom; y++) { if (IsFilled(frame.GetPixel(x, y), fill, empty, tolerance)) { run++; } else { break; } }
                return (roi.Height, run);
            }
            default: // BottomToTop
            {
                var x = roi.X + roi.Width / 2;
                for (var y = roi.Bottom - 1; y >= roi.Top; y--) { if (IsFilled(frame.GetPixel(x, y), fill, empty, tolerance)) { run++; } else { break; } }
                return (roi.Height, run);
            }
        }
    }

    private static bool IsFilled(Color c, Color? fill, Color? empty, int tolerance)
    {
        if (fill is Color f && empty is Color e) { return Distance(c, f) <= Distance(c, e); }
        if (fill is Color fillOnly) { return Distance(c, fillOnly) <= tolerance; }
        return Distance(c, empty!.Value) > tolerance; // only Empty set: filled = NOT the empty color
    }

    private static int Distance(Color a, Color b)
        => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
}
