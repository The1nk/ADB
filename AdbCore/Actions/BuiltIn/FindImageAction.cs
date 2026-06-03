using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Finds a template image within the target window and writes its location (region edges,
/// center, a random in-region point, and the score) to run variables under a configurable prefix.
/// "Not found" is a failed result so the engine can retry (per RetryPolicy) and route onFailure.</summary>
public sealed class FindImageAction : ScreenActionBase
{
    private readonly ITemplateMatcher _matcher;
    private readonly IRandomSource _random;

    public FindImageAction(IWindowCapture capture, ITemplateMatcher matcher, IRandomSource random)
        : base(capture)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(random);
        _matcher = matcher;
        _random = random;
    }

    public override string TypeKey => "screen.findImage";
    public override string DisplayName => "Find Image";
    public override string Description => "Finds a template image within the target window and writes its location to variables.";

    public override List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TemplatePathField(),
        ConfidenceField(),
        ResultVarField(),
    ];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var templatePath = ConfigValues.GetString(context.Action.Config, TemplatePathKey);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return Task.FromResult(ActionResult.Fail("Find Image: a template image path is required."));
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, ConfidenceKey, DefaultConfidence);
        var prefix = ConfigValues.GetString(context.Action.Config, ResultVarKey, DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = DefaultResultVar;
        }

        if (CaptureAndMatch(context, hwnd, _matcher, templatePath, confidence) is not MatchResult m)
        {
            return Task.FromResult(ActionResult.Fail("Find Image: no match at or above the configured confidence."));
        }

        WriteMatchVariables(context, m, prefix, _random);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
