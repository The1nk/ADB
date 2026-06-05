using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>OCRs the target window (or region) and writes the recognized text to a variable.</summary>
public sealed class ReadTextAction : ScreenOcrActionBase
{
    public ReadTextAction(IWindowCapture capture, IOcrEngine ocr) : base(capture, ocr) { }

    public override string TypeKey => "screen.readText";
    public override string DisplayName => "Read Text";
    public override string Description => "Reads text from the target window (or region) into a variable.";
    public override bool SupportsRetry => false;
    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };

    protected override IEnumerable<ConfigField> ActionConfigFields => [ResultVarField("text")];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var resultVar = ConfigValues.GetString(context.Action.Config, ResultVarKey, "text");
        if (string.IsNullOrWhiteSpace(resultVar)) { resultVar = "text"; }

        var result = RecognizeWindow(context, hwnd);
        context.Context.Variables[resultVar] = result.Text.Trim();
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
