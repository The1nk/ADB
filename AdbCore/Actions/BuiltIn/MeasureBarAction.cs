using AdbCore.Execution;
using AdbCore.Screen;
using AdbCore.Targets;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Reads a solid-fill bar's value from the target window: captures (or reuses a stored frame), scans
/// the ROI via <see cref="BarMeasureCore"/>, and writes the integer value + fraction to run variables.</summary>
public sealed class MeasureBarAction : IActionDefinition, IActionExecutor
{
    private readonly IWindowCapture _capture;

    public MeasureBarAction(IWindowCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
    }

    public string TypeKey => "screen.measureBar";
    public string DisplayName => "Measure Bar";
    public string Category => "Screen";
    public string Description => "Reads a solid-fill bar's value (0..Max) by scanning its region for the filled run.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } =
    [
        .. BarMeasureCore.Fields(),
        .. TemplateMatchCore.RegionFields(),
        .. FrameSourceConfig.Fields(),
        new ConfigField
        {
            Key = ScreenActionBase.CaptureMethodKey, Label = "Capture Method", Type = ConfigFieldType.Enum,
            DefaultValue = nameof(ScreenCaptureMethod.Auto),
            Options = new() { nameof(ScreenCaptureMethod.Auto), nameof(ScreenCaptureMethod.BitBlt) },
        },
    ];
    public bool SupportsRetry => true;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var wh = TargetResolution.ResolveHandle<IWindowHandle>(context);
        if (wh?.GetLiveHandle() is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var method = string.Equals(
            ConfigValues.GetString(context.Action.Config, ScreenActionBase.CaptureMethodKey, nameof(ScreenCaptureMethod.Auto)),
            nameof(ScreenCaptureMethod.BitBlt), StringComparison.OrdinalIgnoreCase)
            ? ScreenCaptureMethod.BitBlt : ScreenCaptureMethod.Auto;

        var frame = FrameSourceResolver.Acquire(context, () => _capture.Capture(hwnd, method));
        var result = BarMeasureCore.Measure(frame, context.Action.Config);

        var prefix = ConfigValues.GetString(context.Action.Config, BarMeasureCore.ResultVarKey, BarMeasureCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = BarMeasureCore.DefaultResultVar; }
        BarMeasureCore.WriteResult(context.Context.Variables, prefix, result);
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
