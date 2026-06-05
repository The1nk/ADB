using System.Drawing;
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
    public const string RegionXKey = TemplateMatchCore.RegionXKey;
    public const string RegionYKey = TemplateMatchCore.RegionYKey;
    public const string RegionWidthKey = TemplateMatchCore.RegionWidthKey;
    public const string RegionHeightKey = TemplateMatchCore.RegionHeightKey;
    public const string SuccessPort = TemplateMatchCore.SuccessPort;
    public const string FailurePort = TemplateMatchCore.FailurePort;

    public const string TemplatePathKey = TemplateMatchCore.TemplatePathKey;
    public const string ConfidenceKey = TemplateMatchCore.ConfidenceKey;
    public const string ResultVarKey = TemplateMatchCore.ResultVarKey;
    public const double DefaultConfidence = TemplateMatchCore.DefaultConfidence;
    public const string DefaultResultVar = TemplateMatchCore.DefaultResultVar;

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
        .. TemplateMatchCore.RegionFields(),
    ];

    public abstract Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct);

    // Shared config-field factories for the match actions.
    protected static ConfigField TemplatePathField() => TemplateMatchCore.TemplatePathField();
    protected static ConfigField ConfidenceField() => TemplateMatchCore.ConfidenceField();
    protected static ConfigField ResultVarField() => TemplateMatchCore.ResultVarField();

    /// <summary>Resolves the action's target HWND: the explicit TargetId, or the sole target if unset.</summary>
    protected static IntPtr? ResolveWindow(ActionExecutionContext context)
        => TargetResolution.ResolveHandle<IntPtr>(context);

    private ScreenCaptureMethod CaptureMethodOf(ActionExecutionContext context)
        => string.Equals(
               ConfigValues.GetString(context.Action.Config, CaptureMethodKey, nameof(ScreenCaptureMethod.Auto)),
               nameof(ScreenCaptureMethod.BitBlt), StringComparison.OrdinalIgnoreCase)
           ? ScreenCaptureMethod.BitBlt : ScreenCaptureMethod.Auto;

    /// <summary>Reads + clamps the ROI fields against the client size; null when no usable region.</summary>
    protected static Rectangle? ResolveRegion(ActionExecutionContext context, int clientWidth, int clientHeight)
        => TemplateMatchCore.ResolveRegion(context.Action.Config, clientWidth, clientHeight);

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

    /// <summary>Captures the window's client area via the chosen method, then crops to any ROI, matches the
    /// template, and returns the match in full-window client coordinates (null if none ≥ confidence).</summary>
    protected MatchResult? CaptureAndMatch(ActionExecutionContext context, IntPtr hwnd, ITemplateMatcher matcher, string templatePath, double confidence)
    {
        using var shot = _capture.Capture(hwnd, CaptureMethodOf(context));
        return TemplateMatchCore.MatchInRegion(shot, context.Action.Config, matcher, templatePath, confidence);
    }

    /// <summary>Writes a match's region edges, center, a random in-region point, and the score to run
    /// variables under <paramref name="prefix"/> (all client-relative integers, as InvariantCulture strings).</summary>
    protected static void WriteMatchVariables(ActionExecutionContext context, MatchResult m, string prefix, IRandomSource random)
        => TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, random);
}
