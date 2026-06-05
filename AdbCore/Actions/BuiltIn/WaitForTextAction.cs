using System.Diagnostics;
using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Polls the target window until the text appears or the timeout elapses.</summary>
public sealed class WaitForTextAction : ScreenOcrActionBase
{
    public const string TimeoutMsKey = "timeoutMs";
    public const string PollIntervalMsKey = "pollIntervalMs";
    public const int DefaultTimeoutMs = 5000;
    public const int DefaultPollIntervalMs = 250;

    private readonly IRandomSource _random;

    public WaitForTextAction(IWindowCapture capture, IOcrEngine ocr, IRandomSource random) : base(capture, ocr)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public override string TypeKey => "screen.waitForText";
    public override string DisplayName => "Wait for Text";
    public override string Description => "Polls the target window until the text appears or the timeout elapses.";
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

        var target = ConfigValues.GetString(context.Action.Config, TextKey);
        if (string.IsNullOrWhiteSpace(target))
        {
            return ActionResult.Fail("Wait for Text: a target text string is required.");
        }

        var prefix = ConfigValues.GetString(context.Action.Config, ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = TemplateMatchCore.DefaultResultVar; }
        var minConfidence = ConfigValues.GetDouble(context.Action.Config, MinConfidenceKey, 0);
        var timeoutMs = Math.Max(0, ConfigValues.GetInt(context.Action.Config, TimeoutMsKey, DefaultTimeoutMs));
        var pollMs = Math.Max(1, ConfigValues.GetInt(context.Action.Config, PollIntervalMsKey, DefaultPollIntervalMs));

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var result = RecognizeWindow(context, hwnd);
            if (OcrCore.FindWord(result, target, minConfidence) is MatchResult m)
            {
                TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
                return ActionResult.Ok(SuccessPort);
            }
            if (elapsed.ElapsedMilliseconds >= timeoutMs)
            {
                return ActionResult.Fail($"Wait for Text: '{target}' did not appear within {timeoutMs} ms.");
            }
            await Task.Delay(pollMs, ct);
        }
    }
}
