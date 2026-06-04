using System.Drawing;
using System.Globalization;
using AdbCore.Actions;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Capture-source-independent template-matching core shared by the Screen (HWND capture) and
/// Android (framebuffer capture) image actions: the shared config keys/fields, ROI resolution, the
/// crop + match + offset-back step, and writing match variables. Keeps the two action families'
/// output contracts identical by construction.</summary>
public static class TemplateMatchCore
{
    public const string TemplatePathKey = "templatePath";
    public const string ConfidenceKey = "confidence";
    public const string ResultVarKey = "resultVar";
    public const string RegionXKey = "regionX";
    public const string RegionYKey = "regionY";
    public const string RegionWidthKey = "regionWidth";
    public const string RegionHeightKey = "regionHeight";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";
    public const double DefaultConfidence = 0.8;
    public const string DefaultResultVar = "match";

    public static ConfigField TemplatePathField() => new() { Key = TemplatePathKey, Label = "Template Image", Type = ConfigFieldType.ImagePath };
    public static ConfigField ConfidenceField() => new() { Key = ConfidenceKey, Label = "Confidence", Type = ConfigFieldType.Number, DefaultValue = DefaultConfidence };
    public static ConfigField ResultVarField() => new() { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar };

    /// <summary>The four ROI fields, shown after an action's own fields.</summary>
    public static IEnumerable<ConfigField> RegionFields() =>
    [
        new ConfigField { Key = RegionXKey, Label = "Region X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionYKey, Label = "Region Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionWidthKey, Label = "Region Width", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionHeightKey, Label = "Region Height", Type = ConfigFieldType.Number, DefaultValue = 0 },
    ];

    /// <summary>Reads + clamps the ROI fields against the haystack size; null when no usable region.</summary>
    public static Rectangle? ResolveRegion(IReadOnlyDictionary<string, object> config, int width, int height)
    {
        var w = ConfigValues.GetInt(config, RegionWidthKey, 0);
        var h = ConfigValues.GetInt(config, RegionHeightKey, 0);
        if (w <= 0 || h <= 0 || width <= 0 || height <= 0)
        {
            return null;
        }

        var x = Math.Clamp(ConfigValues.GetInt(config, RegionXKey, 0), 0, width - 1);
        var y = Math.Clamp(ConfigValues.GetInt(config, RegionYKey, 0), 0, height - 1);
        w = Math.Min(w, width - x);
        h = Math.Min(h, height - y);
        return w > 0 && h > 0 ? new Rectangle(x, y, w, h) : null;
    }

    /// <summary>Crops the haystack to the configured ROI (if any), matches the template, and returns the
    /// match in full-haystack coordinates (null when none ≥ confidence). Does not dispose the haystack.</summary>
    public static MatchResult? MatchInRegion(Bitmap haystack, IReadOnlyDictionary<string, object> config, ITemplateMatcher matcher, string templatePath, double confidence)
    {
        var region = ResolveRegion(config, haystack.Width, haystack.Height);
        if (region is not Rectangle roi)
        {
            return matcher.Match(haystack, templatePath, confidence);
        }

        using var crop = haystack.Clone(roi, haystack.PixelFormat);
        var hit = matcher.Match(crop, templatePath, confidence);
        return hit is MatchResult m ? m with { X = m.X + roi.X, Y = m.Y + roi.Y } : null;
    }

    /// <summary>Writes a match's region edges, center, a random in-region point, and the score to
    /// <paramref name="variables"/> under <paramref name="prefix"/> (integers as InvariantCulture strings).</summary>
    public static void WriteMatchVariables(IDictionary<string, object> variables, MatchResult m, string prefix, IRandomSource random)
    {
        var left = m.X;
        var top = m.Y;
        var right = m.X + m.Width;
        var bottom = m.Y + m.Height;
        variables[$"{prefix}Left"] = Str(left);
        variables[$"{prefix}Top"] = Str(top);
        variables[$"{prefix}Right"] = Str(right);
        variables[$"{prefix}Bottom"] = Str(bottom);
        variables[$"{prefix}CenterX"] = Str(m.X + m.Width / 2);
        variables[$"{prefix}CenterY"] = Str(m.Y + m.Height / 2);
        variables[$"{prefix}RandX"] = Str(random.Next(left, right));
        variables[$"{prefix}RandY"] = Str(random.Next(top, bottom));
        variables[$"{prefix}Confidence"] = m.Score.ToString(CultureInfo.InvariantCulture);
    }

    private static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);
}
