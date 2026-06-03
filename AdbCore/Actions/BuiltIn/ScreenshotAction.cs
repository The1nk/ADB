using System.Drawing.Imaging;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Captures the target window's client area (or the configured region) and saves it as a PNG.</summary>
public sealed class ScreenshotAction : ScreenActionBase
{
    public const string OutputPathKey = "outputPath";

    public ScreenshotAction(IWindowCapture capture) : base(capture)
    {
    }

    public override string TypeKey => "screen.screenshot";
    public override string DisplayName => "Screenshot";
    public override string Description => "Captures the target window (optionally a region) and saves it as a PNG.";

    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public override bool SupportsRetry => false;

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        new ConfigField { Key = OutputPathKey, Label = "Output Path", Type = ConfigFieldType.FilePath },
    ];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var outputPath = ConfigValues.GetString(context.Action.Config, OutputPathKey);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Task.FromResult(ActionResult.Fail("Screenshot: an output path is required."));
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var bitmap = CaptureRegion(context, hwnd, out _, out _);
        bitmap.Save(outputPath, ImageFormat.Png);

        return Task.FromResult(ActionResult.Ok("out"));
    }
}
