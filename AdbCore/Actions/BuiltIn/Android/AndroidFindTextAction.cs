using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Finds a target string on the device screen via OCR and writes its location (the same match
/// variables as Find Image) under a prefix. Not found is a failed result (engine retry / onFailure).</summary>
public sealed class AndroidFindTextAction : AndroidOcrActionBase
{
    private readonly IRandomSource _random;

    public AndroidFindTextAction(IOcrEngine ocr, IRandomSource random) : base(ocr)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public override string TypeKey => "android.findText";
    public override string DisplayName => "Find Text (Android)";
    public override string Description => "Finds a text string on the device screen and writes its location to variables.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        OcrCore.TextField(),
        OcrCore.ResultVarField(TemplateMatchCore.DefaultResultVar),
        OcrCore.MinConfidenceField(),
    ];

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
            return Task.FromResult(ActionResult.Fail("Find Text (Android): a target text string is required."));
        }

        var prefix = ConfigValues.GetString(context.Action.Config, OcrCore.ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = TemplateMatchCore.DefaultResultVar; }
        var minConfidence = ConfigValues.GetDouble(context.Action.Config, OcrCore.MinConfidenceKey, 0);

        var result = RecognizeDevice(context, device);
        if (OcrCore.FindWord(result, target, minConfidence) is not MatchResult m)
        {
            return Task.FromResult(ActionResult.Fail($"Find Text (Android): '{target}' not found."));
        }

        TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
