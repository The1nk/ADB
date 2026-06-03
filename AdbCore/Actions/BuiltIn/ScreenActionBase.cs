using System.Drawing;
using System.Globalization;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Screen actions: resolves the target window HWND, exposes the Capture Method
/// + region-of-interest config fields, and provides capture/match helpers that return matches in
/// full-window client coordinates. Requires only <see cref="IWindowCapture"/>; matching actions pass
/// their own <see cref="ITemplateMatcher"/> to <see cref="CaptureAndMatch"/>.</summary>
public abstract class ScreenActionBase : IActionDefinition, IActionExecutor
{
    public const string CaptureMethodKey = "captureMethod";
    public const string RegionXKey = "regionX";
    public const string RegionYKey = "regionY";
    public const string RegionWidthKey = "regionWidth";
    public const string RegionHeightKey = "regionHeight";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    // Shared match config (used by Find Image, Wait for Image, Assert Image Absent).
    public const string TemplatePathKey = "templatePath";
    public const string ConfidenceKey = "confidence";
    public const string ResultVarKey = "resultVar";
    public const double DefaultConfidence = 0.8;
    public const string DefaultResultVar = "match";

    private readonly IWindowCapture _capture;
    private List<ConfigField>? _configFields;

    protected ScreenActionBase(IWindowCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
    }

    public abstract string TypeKey { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public string Category => "Screen";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public abstract List<PortDefinition> OutputPorts { get; }
    public virtual bool SupportsRetry => true;

    /// <summary>The action's own config fields, shown before the shared Capture Method + ROI fields.</summary>
    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public List<ConfigField> ConfigFields => _configFields ??=
    [
        .. ActionConfigFields,
        new ConfigField
        {
            Key = CaptureMethodKey, Label = "Capture Method", Type = ConfigFieldType.Enum,
            DefaultValue = nameof(ScreenCaptureMethod.Auto),
            Options = new() { nameof(ScreenCaptureMethod.Auto), nameof(ScreenCaptureMethod.BitBlt) },
        },
        new ConfigField { Key = RegionXKey, Label = "Region X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionYKey, Label = "Region Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionWidthKey, Label = "Region Width", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = RegionHeightKey, Label = "Region Height", Type = ConfigFieldType.Number, DefaultValue = 0 },
    ];

    public abstract Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct);

    // Shared config-field factories for the match actions.
    protected static ConfigField TemplatePathField() => new() { Key = TemplatePathKey, Label = "Template Image", Type = ConfigFieldType.ImagePath };
    protected static ConfigField ConfidenceField() => new() { Key = ConfidenceKey, Label = "Confidence", Type = ConfigFieldType.Number, DefaultValue = DefaultConfidence };
    protected static ConfigField ResultVarField() => new() { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar };

    /// <summary>Resolves the action's target HWND: the explicit TargetId, or the sole target if unset.</summary>
    protected static IntPtr? ResolveWindow(ActionExecutionContext context)
    {
        var targets = context.Context.Targets;
        ResolvedTarget? target = context.Action.TargetId is Guid id
            ? targets.TryGetValue(id, out var t) ? t : null
            : targets.Count == 1 ? targets.Values.First() : null;

        return target?.Handle as IntPtr?;
    }

    private ScreenCaptureMethod CaptureMethodOf(ActionExecutionContext context)
        => string.Equals(
               ConfigValues.GetString(context.Action.Config, CaptureMethodKey, nameof(ScreenCaptureMethod.Auto)),
               nameof(ScreenCaptureMethod.BitBlt), StringComparison.OrdinalIgnoreCase)
           ? ScreenCaptureMethod.BitBlt : ScreenCaptureMethod.Auto;

    /// <summary>Reads + clamps the ROI fields against the client size; null when no usable region.</summary>
    protected static Rectangle? ResolveRegion(ActionExecutionContext context, int clientWidth, int clientHeight)
    {
        var w = ConfigValues.GetInt(context.Action.Config, RegionWidthKey, 0);
        var h = ConfigValues.GetInt(context.Action.Config, RegionHeightKey, 0);
        if (w <= 0 || h <= 0 || clientWidth <= 0 || clientHeight <= 0)
        {
            return null;
        }

        var x = Math.Clamp(ConfigValues.GetInt(context.Action.Config, RegionXKey, 0), 0, clientWidth - 1);
        var y = Math.Clamp(ConfigValues.GetInt(context.Action.Config, RegionYKey, 0), 0, clientHeight - 1);
        w = Math.Min(w, clientWidth - x);
        h = Math.Min(h, clientHeight - y);
        return w > 0 && h > 0 ? new Rectangle(x, y, w, h) : null;
    }

    /// <summary>Captures the window's client area via the chosen method, cropping to the ROI when set.
    /// Returns the (possibly cropped) bitmap and the ROI offset (0,0 when no ROI). Caller disposes.</summary>
    protected Bitmap CaptureRegion(ActionExecutionContext context, IntPtr hwnd, out int offsetX, out int offsetY)
    {
        var shot = _capture.Capture(hwnd, CaptureMethodOf(context));
        var region = ResolveRegion(context, shot.Width, shot.Height);
        if (region is not Rectangle roi)
        {
            offsetX = 0;
            offsetY = 0;
            return shot;
        }

        using (shot)
        {
            offsetX = roi.X;
            offsetY = roi.Y;
            return shot.Clone(roi, shot.PixelFormat);
        }
    }

    /// <summary>Captures (with any ROI crop), matches the template, and returns the match in full-window
    /// client coordinates (null if none ≥ confidence). Disposes the capture.</summary>
    protected MatchResult? CaptureAndMatch(ActionExecutionContext context, IntPtr hwnd, ITemplateMatcher matcher, string templatePath, double confidence)
    {
        using var region = CaptureRegion(context, hwnd, out var offsetX, out var offsetY);
        var hit = matcher.Match(region, templatePath, confidence);
        return hit is MatchResult m ? m with { X = m.X + offsetX, Y = m.Y + offsetY } : null;
    }

    /// <summary>Writes a match's region edges, center, a random in-region point, and the score to run
    /// variables under <paramref name="prefix"/> (all client-relative integers, as InvariantCulture strings).</summary>
    protected static void WriteMatchVariables(ActionExecutionContext context, MatchResult m, string prefix, IRandomSource random)
    {
        var left = m.X;
        var top = m.Y;
        var right = m.X + m.Width;
        var bottom = m.Y + m.Height;
        var vars = context.Context.Variables;
        vars[$"{prefix}Left"] = Str(left);
        vars[$"{prefix}Top"] = Str(top);
        vars[$"{prefix}Right"] = Str(right);
        vars[$"{prefix}Bottom"] = Str(bottom);
        vars[$"{prefix}CenterX"] = Str(m.X + m.Width / 2);
        vars[$"{prefix}CenterY"] = Str(m.Y + m.Height / 2);
        vars[$"{prefix}RandX"] = Str(random.Next(left, right));
        vars[$"{prefix}RandY"] = Str(random.Next(top, bottom));
        vars[$"{prefix}Confidence"] = m.Score.ToString(CultureInfo.InvariantCulture);
    }

    private static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);
}
