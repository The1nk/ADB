using System.IO;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Captures the device screen and saves it as a PNG.</summary>
public sealed class AndroidScreenshotAction : AndroidActionBase
{
    public override string TypeKey => "android.screenshot";
    public override string DisplayName => "Screenshot (Android)";
    public override string Description => "Captures the device screen and saves it to a PNG file.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "outputPath", Label = "Save To", Type = ConfigFieldType.FilePath, DefaultValue = "" },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var path = ConfigValues.GetString(context.Action.Config, "outputPath");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(ActionResult.Fail("Screenshot (Android): an output file path is required."));
        }

        File.WriteAllBytes(path, device.Screenshot());
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
