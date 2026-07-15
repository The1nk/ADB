using System.Drawing;
using System.IO;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Captures the bound device's screen once into a named frame in the run's frame store, so
/// downstream readers set to the "Stored" source can reuse it instead of taking another screenshot.</summary>
public sealed class AndroidCaptureFrameAction : AndroidActionBase
{
    public override string TypeKey => "android.captureFrame";
    public override string DisplayName => "Capture Frame (Android)";
    public override string Description => "Captures the device screen once into a named frame that later readers can reuse.";
    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public override List<ConfigField> ConfigFields { get; } = new() { FrameSourceConfig.FrameNameField() };
    public override bool SupportsRetry => true;

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        using var ms = new MemoryStream(device.Screenshot());
        using var frame = new Bitmap(ms);
        context.Context.Frames.Set(FrameSourceConfig.FrameNameOf(context.Action.Config), FrameSnapshot.FromBitmap(frame));
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
