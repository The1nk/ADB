using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Succeeds only when the target text is NOT present in the target window (present → Fail, so
/// with a RetryPolicy it becomes "wait until the text is gone").</summary>
public sealed class AssertTextAbsentAction : ScreenOcrActionBase
{
    public AssertTextAbsentAction(IWindowCapture capture, IOcrEngine ocr) : base(capture, ocr) { }

    public override string TypeKey => "screen.assertTextAbsent";
    public override string DisplayName => "Assert Text Absent";
    public override string Description => "Succeeds when the target text is not present in the target window.";
    public override List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    protected override IEnumerable<ConfigField> ActionConfigFields => [TextField(), MinConfidenceField()];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var target = ConfigValues.GetString(context.Action.Config, TextKey);
        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(ActionResult.Fail("Assert Text Absent: a target text string is required."));
        }

        var minConfidence = ConfigValues.GetDouble(context.Action.Config, MinConfidenceKey, 0);
        var result = RecognizeWindow(context, hwnd);

        return OcrCore.FindWord(result, target, minConfidence) is MatchResult
            ? Task.FromResult(ActionResult.Fail($"Assert Text Absent: '{target}' is present."))
            : Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
