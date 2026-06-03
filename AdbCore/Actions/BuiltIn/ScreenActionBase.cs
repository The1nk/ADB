using System.Drawing;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Screen actions: resolves the target window HWND, exposes the Capture Method
/// + region-of-interest config fields, and provides a capture→(crop)→match helper that returns matches in
/// full-window client coordinates. Mirrors <see cref="InputActionBase"/>'s structure.</summary>
public abstract class ScreenActionBase : IActionDefinition, IActionExecutor
{
    public const string CaptureMethodKey = "captureMethod";
    public const string RegionXKey = "regionX";
    public const string RegionYKey = "regionY";
    public const string RegionWidthKey = "regionWidth";
    public const string RegionHeightKey = "regionHeight";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    private readonly IWindowCapture _capture;
    private readonly ITemplateMatcher _matcher;
    private List<ConfigField>? _configFields;

    protected ScreenActionBase(IWindowCapture capture, ITemplateMatcher matcher)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(matcher);
        _capture = capture;
        _matcher = matcher;
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

    /// <summary>Captures the window, applies any ROI crop, matches the template, and returns the match in
    /// full-window client coordinates (null if none ≥ confidence). Disposes the capture.</summary>
    protected MatchResult? CaptureAndMatch(ActionExecutionContext context, IntPtr hwnd, string templatePath, double confidence)
    {
        using var shot = _capture.Capture(hwnd, CaptureMethodOf(context));
        var region = ResolveRegion(context, shot.Width, shot.Height);
        if (region is not Rectangle roi)
        {
            return _matcher.Match(shot, templatePath, confidence);
        }

        using var crop = shot.Clone(roi, shot.PixelFormat);
        var hit = _matcher.Match(crop, templatePath, confidence);
        return hit is MatchResult m ? m with { X = m.X + roi.X, Y = m.Y + roi.Y } : null;
    }
}
