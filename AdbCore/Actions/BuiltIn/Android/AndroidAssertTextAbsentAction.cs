using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Succeeds only when the target text is NOT present on the device screen (present → Fail).</summary>
public sealed class AndroidAssertTextAbsentAction : AndroidOcrActionBase
{
    public AndroidAssertTextAbsentAction(IOcrEngine ocr) : base(ocr) { }

    public override string TypeKey => "android.assertTextAbsent";
    public override string DisplayName => "Assert Text Absent (Android)";
    public override string Description => "Succeeds when the target text is not present on the device screen.";

    protected override IEnumerable<ConfigField> ActionConfigFields => [OcrCore.TextField(), OcrCore.MinConfidenceField()];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var target = ConfigValues.GetString(context.Action.Config, OcrCore.TextKey);
        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(ActionResult.Fail("Assert Text Absent (Android): a target text string is required."));
        }

        var minConfidence = ConfigValues.GetDouble(context.Action.Config, OcrCore.MinConfidenceKey, 0);
        var result = RecognizeDevice(context, device);

        return OcrCore.FindWord(result, target, minConfidence) is MatchResult
            ? Task.FromResult(ActionResult.Fail($"Assert Text Absent (Android): '{target}' is present."))
            : Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
