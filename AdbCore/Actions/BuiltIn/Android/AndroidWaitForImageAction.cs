using System.Diagnostics;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Polls the device screen until the template appears or the timeout elapses. On success writes
/// the same match variables as Find Image (Android); on timeout returns a failed result.</summary>
public sealed class AndroidWaitForImageAction : AndroidImageActionBase
{
    public const string TimeoutMsKey = "timeoutMs";
    public const string PollIntervalMsKey = "pollIntervalMs";
    public const int DefaultTimeoutMs = 5000;
    public const int DefaultPollIntervalMs = 250;

    private readonly ITemplateMatcher _matcher;
    private readonly IRandomSource _random;

    public AndroidWaitForImageAction(ITemplateMatcher matcher, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(random);
        _matcher = matcher;
        _random = random;
    }

    public override string TypeKey => "android.waitForImage";
    public override string DisplayName => "Wait for Image (Android)";
    public override string Description => "Polls the device screen until the template appears or the timeout elapses.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TemplateMatchCore.TemplatePathField(),
        TemplateMatchCore.ConfidenceField(),
        TemplateMatchCore.ResultVarField(),
        new ConfigField { Key = TimeoutMsKey, Label = "Timeout (ms)", Type = ConfigFieldType.Number, DefaultValue = DefaultTimeoutMs },
        new ConfigField { Key = PollIntervalMsKey, Label = "Poll Interval (ms)", Type = ConfigFieldType.Number, DefaultValue = DefaultPollIntervalMs },
    ];

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return RequiresDevice();
        }

        var templatePath = ConfigValues.GetString(context.Action.Config, TemplateMatchCore.TemplatePathKey);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return ActionResult.Fail("Wait for Image (Android): a template image path is required.");
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, TemplateMatchCore.ConfidenceKey, TemplateMatchCore.DefaultConfidence);
        var prefix = ConfigValues.GetString(context.Action.Config, TemplateMatchCore.ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = TemplateMatchCore.DefaultResultVar;
        }

        var timeoutMs = Math.Max(0, ConfigValues.GetInt(context.Action.Config, TimeoutMsKey, DefaultTimeoutMs));
        var pollMs = Math.Max(1, ConfigValues.GetInt(context.Action.Config, PollIntervalMsKey, DefaultPollIntervalMs));

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (CaptureAndMatch(context, device, _matcher, templatePath, confidence) is MatchResult m)
            {
                TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
                return ActionResult.Ok(SuccessPort);
            }

            if (elapsed.ElapsedMilliseconds >= timeoutMs)
            {
                return ActionResult.Fail($"Wait for Image (Android): template did not appear within {timeoutMs} ms.");
            }

            await Task.Delay(pollMs, ct);
        }
    }
}
