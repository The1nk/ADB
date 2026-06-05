using System.Diagnostics;
using AdbCore.Execution;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Polls the device screen until the text appears or the timeout elapses.</summary>
public sealed class AndroidWaitForTextAction : AndroidOcrActionBase
{
    public const string TimeoutMsKey = "timeoutMs";
    public const string PollIntervalMsKey = "pollIntervalMs";
    public const int DefaultTimeoutMs = 5000;
    public const int DefaultPollIntervalMs = 250;

    private readonly IRandomSource _random;

    public AndroidWaitForTextAction(IOcrEngine ocr, IRandomSource random) : base(ocr)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public override string TypeKey => "android.waitForText";
    public override string DisplayName => "Wait for Text (Android)";
    public override string Description => "Polls the device screen until the text appears or the timeout elapses.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        OcrCore.TextField(),
        OcrCore.ResultVarField(TemplateMatchCore.DefaultResultVar),
        OcrCore.MinConfidenceField(),
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

        var target = ConfigValues.GetString(context.Action.Config, OcrCore.TextKey);
        if (string.IsNullOrWhiteSpace(target))
        {
            return ActionResult.Fail("Wait for Text (Android): a target text string is required.");
        }

        var prefix = ConfigValues.GetString(context.Action.Config, OcrCore.ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = TemplateMatchCore.DefaultResultVar; }
        var minConfidence = ConfigValues.GetDouble(context.Action.Config, OcrCore.MinConfidenceKey, 0);
        var timeoutMs = Math.Max(0, ConfigValues.GetInt(context.Action.Config, TimeoutMsKey, DefaultTimeoutMs));
        var pollMs = Math.Max(1, ConfigValues.GetInt(context.Action.Config, PollIntervalMsKey, DefaultPollIntervalMs));

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var result = RecognizeDevice(context, device);
            if (OcrCore.FindWord(result, target, minConfidence) is MatchResult m)
            {
                TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
                return ActionResult.Ok(SuccessPort);
            }
            if (elapsed.ElapsedMilliseconds >= timeoutMs)
            {
                return ActionResult.Fail($"Wait for Text (Android): '{target}' did not appear within {timeoutMs} ms.");
            }
            await Task.Delay(pollMs, ct);
        }
    }
}
