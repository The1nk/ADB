using System.Globalization;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Finds a template image within the target window and writes its location (region edges,
/// center, a random in-region point, and the score) to run variables under a configurable prefix.
/// "Not found" is a failed result so the engine can retry (per RetryPolicy) and route onFailure.</summary>
public sealed class FindImageAction : ScreenActionBase
{
    public const string TemplatePathKey = "templatePath";
    public const string ConfidenceKey = "confidence";
    public const string ResultVarKey = "resultVar";
    public const double DefaultConfidence = 0.8;
    public const string DefaultResultVar = "match";

    private readonly IRandomSource _random;

    public FindImageAction(IWindowCapture capture, ITemplateMatcher matcher, IRandomSource random)
        : base(capture, matcher)
    {
        ArgumentNullException.ThrowIfNull(random);
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
        new ConfigField { Key = TemplatePathKey, Label = "Template Image", Type = ConfigFieldType.ImagePath },
        new ConfigField { Key = ConfidenceKey, Label = "Confidence", Type = ConfigFieldType.Number, DefaultValue = DefaultConfidence },
        new ConfigField { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar },
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

        if (CaptureAndMatch(context, hwnd, templatePath, confidence) is not MatchResult m)
        {
            return Task.FromResult(ActionResult.Fail("Find Image: no match at or above the configured confidence."));
        }

        var left = m.X;
        var top = m.Y;
        var right = m.X + m.Width;
        var bottom = m.Y + m.Height;
        var vars = context.Context.Variables;
        vars[$"{prefix}Left"] = Str(left);
        vars[$"{prefix}Top"] = Str(top);
        vars[$"{prefix}Right"] = Str(right);
        vars[$"{prefix}Bottom"] = Str(bottom);
        vars[$"{prefix}CenterX"] = Str(m.X + m.Width / 2);
        vars[$"{prefix}CenterY"] = Str(m.Y + m.Height / 2);
        vars[$"{prefix}RandX"] = Str(_random.Next(left, right));
        vars[$"{prefix}RandY"] = Str(_random.Next(top, bottom));
        vars[$"{prefix}Confidence"] = m.Score.ToString(CultureInfo.InvariantCulture);

        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }

    private static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);
}
