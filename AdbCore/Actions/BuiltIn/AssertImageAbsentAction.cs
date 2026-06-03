using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Succeeds only when the template is NOT found in the target window. While the template is
/// present it returns a failed result, so with a RetryPolicy it becomes "wait until the image is gone",
/// and otherwise routes onFailure.</summary>
public sealed class AssertImageAbsentAction : ScreenActionBase
{
    private readonly ITemplateMatcher _matcher;

    public AssertImageAbsentAction(IWindowCapture capture, ITemplateMatcher matcher)
        : base(capture)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        _matcher = matcher;
    }

    public override string TypeKey => "screen.assertImageAbsent";
    public override string DisplayName => "Assert Image Absent";
    public override string Description => "Succeeds when the template is not present in the target window.";

    public override List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TemplatePathField(),
        ConfidenceField(),
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
            return Task.FromResult(ActionResult.Fail("Assert Image Absent: a template image path is required."));
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, ConfidenceKey, DefaultConfidence);

        return CaptureAndMatch(context, hwnd, _matcher, templatePath, confidence) is MatchResult m
            ? Task.FromResult(ActionResult.Fail($"Assert Image Absent: template is present (score {m.Score.ToString(System.Globalization.CultureInfo.InvariantCulture)})."))
            : Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
