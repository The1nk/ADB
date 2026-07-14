using AdbCore.Execution;
using AdbCore.Screen;
using AdbCore.Targets;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Captures the target window's client area once into a named frame in the run's frame store, so
/// downstream Screen readers set to the "Stored" source can reuse it instead of re-capturing.</summary>
public sealed class CaptureFrameAction : IActionDefinition, IActionExecutor
{
    private readonly IWindowCapture _capture;

    public CaptureFrameAction(IWindowCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
    }

    public string TypeKey => "screen.captureFrame";
    public string DisplayName => "Capture Frame";
    public string Category => "Screen";
    public string Description => "Captures the target window once into a named frame that later Screen readers can reuse.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        FrameSourceConfig.FrameNameField(),
        new ConfigField
        {
            Key = ScreenActionBase.CaptureMethodKey, Label = "Capture Method", Type = ConfigFieldType.Enum,
            DefaultValue = nameof(ScreenCaptureMethod.Auto),
            Options = new() { nameof(ScreenCaptureMethod.Auto), nameof(ScreenCaptureMethod.BitBlt) },
        },
    };
    public bool SupportsRetry => true;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var wh = TargetResolution.ResolveHandle<IWindowHandle>(context);
        if (wh?.GetLiveHandle() is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail("Capture Frame requires a resolved Window target (HWND)."));
        }

        var method = string.Equals(
            ConfigValues.GetString(context.Action.Config, ScreenActionBase.CaptureMethodKey, nameof(ScreenCaptureMethod.Auto)),
            nameof(ScreenCaptureMethod.BitBlt), StringComparison.OrdinalIgnoreCase)
            ? ScreenCaptureMethod.BitBlt : ScreenCaptureMethod.Auto;

        using var shot = _capture.Capture(hwnd, method);
        context.Context.Frames.Set(FrameSourceConfig.FrameNameOf(context.Action.Config), FrameSnapshot.FromBitmap(shot));
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
