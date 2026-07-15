using AdbCore.Execution;
using AdbCore.Screen;
using AdbCore.Targets;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Reads a single pixel's color from the target window (fresh capture or a stored frame) into run
/// variables (<c>&lt;prefix&gt;Hex/R/G/B</c>). Read-only; branch on the variables downstream.</summary>
public sealed class GetPixelColorAction : IActionDefinition, IActionExecutor
{
    private readonly IWindowCapture _capture;

    public GetPixelColorAction(IWindowCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
    }

    public string TypeKey => "screen.getPixelColor";
    public string DisplayName => "Get Pixel Color";
    public string Category => "Screen";
    public string Description => "Reads the color of a single pixel into variables (hex + R/G/B).";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } =
    [
        .. PixelReadCore.Fields(),
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
        PixelReadCore.ReadInto(frame, context.Action.Config, context.Context.Variables);
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
