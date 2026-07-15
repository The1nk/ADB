using System.Drawing;
using System.IO;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Reads a single pixel's color from the device screen (fresh capture or a stored frame) into run
/// variables (<c>&lt;prefix&gt;Hex/R/G/B</c>). Read-only; branch on the variables downstream.</summary>
public sealed class AndroidGetPixelColorAction : AndroidActionBase
{
    public override string TypeKey => "android.getPixelColor";
    public override string DisplayName => "Get Pixel Color (Android)";
    public override string Description => "Reads the color of a single pixel on the device screen into variables (hex + R/G/B).";
    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public override List<ConfigField> ConfigFields { get; } =
    [
        .. PixelReadCore.Fields(),
        .. FrameSourceConfig.Fields(),
    ];
    public override bool SupportsRetry => true;

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var frame = FrameSourceResolver.Acquire(context, () =>
        {
            using var ms = new MemoryStream(device.Screenshot());
            using var decoded = new Bitmap(ms);
            return new Bitmap(decoded); // detached copy so the stream can be disposed
        });
        PixelReadCore.ReadInto(frame, context.Action.Config, context.Context.Variables);
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
