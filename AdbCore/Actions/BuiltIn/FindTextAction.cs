using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Finds a target string in the target window via OCR and writes its location (the same match
/// variables as Find Image) under a prefix. Not found is a failed result (engine retry / onFailure).</summary>
public sealed class FindTextAction : ScreenOcrActionBase
{
    private readonly IRandomSource _random;

    public FindTextAction(IWindowCapture capture, IOcrEngine ocr, IRandomSource random) : base(capture, ocr)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public override string TypeKey => "screen.findText";
    public override string DisplayName => "Find Text";
    public override string Description => "Finds a text string in the target window and writes its location to variables.";
    public override List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TextField(),
        ResultVarField(TemplateMatchCore.DefaultResultVar),
        MinConfidenceField(),
    ];

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
            return Task.FromResult(ActionResult.Fail("Find Text: a target text string is required."));
        }

        var prefix = ConfigValues.GetString(context.Action.Config, ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = TemplateMatchCore.DefaultResultVar; }
        var minConfidence = ConfigValues.GetDouble(context.Action.Config, MinConfidenceKey, 0);

        var result = RecognizeWindow(context, hwnd);
        if (OcrCore.FindWord(result, target, minConfidence) is not MatchResult m)
        {
            return Task.FromResult(ActionResult.Fail($"Find Text: '{target}' not found."));
        }

        TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
