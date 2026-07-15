using System.Drawing;
using System.IO;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Reads a solid-fill bar's value from the bound device screen: captures (or reuses a stored frame),
/// scans the ROI via <see cref="BarMeasureCore"/>, and writes the integer value + fraction to run variables.</summary>
public sealed class AndroidMeasureBarAction : AndroidActionBase
{
    public override string TypeKey => "android.measureBar";
    public override string DisplayName => "Measure Bar (Android)";
    public override string Description => "Reads a solid-fill bar's value (0..Max) from the device screen by scanning its region.";
    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public override List<ConfigField> ConfigFields { get; } =
    [
        .. BarMeasureCore.Fields(),
        .. TemplateMatchCore.RegionFields(),
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
        var result = BarMeasureCore.Measure(frame, context.Action.Config);

        var prefix = ConfigValues.GetString(context.Action.Config, BarMeasureCore.ResultVarKey, BarMeasureCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = BarMeasureCore.DefaultResultVar; }
        BarMeasureCore.WriteResult(context.Context.Variables, prefix, result);
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
