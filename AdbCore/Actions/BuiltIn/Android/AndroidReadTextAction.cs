using AdbCore.Execution;
using AdbCore.Ocr;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>OCRs the device screen (or region) and writes the recognized text to a variable.</summary>
public sealed class AndroidReadTextAction : AndroidOcrActionBase
{
    public AndroidReadTextAction(IOcrEngine ocr) : base(ocr) { }

    public override string TypeKey => "android.readText";
    public override string DisplayName => "Read Text (Android)";
    public override string Description => "Reads text from the device screen (or region) into a variable.";
    public override bool SupportsRetry => false;
    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };

    protected override IEnumerable<ConfigField> ActionConfigFields => [OcrCore.ResultVarField("text")];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var resultVar = ConfigValues.GetString(context.Action.Config, OcrCore.ResultVarKey, "text");
        if (string.IsNullOrWhiteSpace(resultVar)) { resultVar = "text"; }

        var result = RecognizeDevice(context, device);
        context.Context.Variables[resultVar] = result.Text.Trim();
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
