using System.Globalization;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Capture-source-independent core for the Get Pixel Color actions: reads one pixel from a
/// <see cref="FrameSnapshot"/> and writes its hex (<c>#RRGGBB</c>) and R/G/B channels to run variables under a
/// configurable prefix (default <c>pixel</c>).</summary>
public static class PixelReadCore
{
    public const string PointXKey = "x";
    public const string PointYKey = "y";
    public const string ResultVarKey = "resultVar";
    public const string DefaultResultVar = "pixel";

    /// <summary>The point + result-var fields. The action appends the shared Source (+ capture method) fields.</summary>
    public static IEnumerable<ConfigField> Fields() =>
    [
        new ConfigField { Key = PointXKey, Label = "X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = PointYKey, Label = "Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar },
    ];

    /// <summary>Reads the pixel at (x,y) and writes <c>&lt;prefix&gt;Hex/R/G/B</c> into <paramref name="variables"/>.
    /// Throws <see cref="ArgumentException"/> when the point is outside the frame.</summary>
    public static void ReadInto(FrameSnapshot frame, IReadOnlyDictionary<string, object> config, IDictionary<string, object> variables)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var x = ConfigValues.GetInt(config, PointXKey, 0);
        var y = ConfigValues.GetInt(config, PointYKey, 0);
        if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height)
        {
            throw new ArgumentException($"Get Pixel Color point ({x},{y}) is outside the {frame.Width}x{frame.Height} frame.");
        }

        var prefix = ConfigValues.GetString(config, ResultVarKey, DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = DefaultResultVar; }

        var c = frame.GetPixel(x, y);
        variables[$"{prefix}Hex"] = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        variables[$"{prefix}R"] = c.R.ToString(CultureInfo.InvariantCulture);
        variables[$"{prefix}G"] = c.G.ToString(CultureInfo.InvariantCulture);
        variables[$"{prefix}B"] = c.B.ToString(CultureInfo.InvariantCulture);
    }
}
