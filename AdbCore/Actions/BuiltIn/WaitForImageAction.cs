using System.Diagnostics;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Polls the target window until the template appears or the timeout elapses. On success writes
/// the same match variables as Find Image; on timeout returns a failed result (engine retry / onFailure).</summary>
public sealed class WaitForImageAction : ScreenActionBase
{
    public const string TimeoutMsKey = "timeoutMs";
    public const string PollIntervalMsKey = "pollIntervalMs";
    public const int DefaultTimeoutMs = 5000;
    public const int DefaultPollIntervalMs = 250;

    private readonly ITemplateMatcher _matcher;
    private readonly IRandomSource _random;

    public WaitForImageAction(IWindowCapture capture, ITemplateMatcher matcher, IRandomSource random)
        : base(capture)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(random);
        _matcher = matcher;
        _random = random;
    }

    public override string TypeKey => "screen.waitForImage";
    public override string DisplayName => "Wait for Image";
    public override string Description => "Polls the target window until the template appears or the timeout elapses.";

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
        new ConfigField { Key = TimeoutMsKey, Label = "Timeout (ms)", Type = ConfigFieldType.Number, DefaultValue = DefaultTimeoutMs },
        new ConfigField { Key = PollIntervalMsKey, Label = "Poll Interval (ms)", Type = ConfigFieldType.Number, DefaultValue = DefaultPollIntervalMs },
    ];

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND).");
        }

        var templatePath = ConfigValues.GetString(context.Action.Config, TemplatePathKey);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return ActionResult.Fail("Wait for Image: a template image path is required.");
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, ConfidenceKey, DefaultConfidence);
        var prefix = ConfigValues.GetString(context.Action.Config, ResultVarKey, DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = DefaultResultVar;
        }

        var timeoutMs = Math.Max(0, ConfigValues.GetInt(context.Action.Config, TimeoutMsKey, DefaultTimeoutMs));
        var pollMs = Math.Max(1, ConfigValues.GetInt(context.Action.Config, PollIntervalMsKey, DefaultPollIntervalMs));

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (CaptureAndMatch(context, hwnd, _matcher, templatePath, confidence) is MatchResult m)
            {
                WriteMatchVariables(context, m, prefix, _random);
                return ActionResult.Ok(SuccessPort);
            }

            if (elapsed.ElapsedMilliseconds >= timeoutMs)
            {
                return ActionResult.Fail($"Wait for Image: template did not appear within {timeoutMs} ms.");
            }

            await Task.Delay(pollMs, ct);
        }
    }
}
