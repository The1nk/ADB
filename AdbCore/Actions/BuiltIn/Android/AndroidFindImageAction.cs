using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Finds a template image on the bound device's screen and writes its location (region edges,
/// center, a random in-region point, and the score) to run variables under a configurable prefix.
/// "Not found" is a failed result so the engine can retry (per RetryPolicy) and route onFailure.</summary>
public sealed class AndroidFindImageAction : AndroidImageActionBase
{
    private readonly ITemplateMatcher _matcher;
    private readonly IRandomSource _random;

    public AndroidFindImageAction(ITemplateMatcher matcher, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(random);
        _matcher = matcher;
        _random = random;
    }

    public override string TypeKey => "android.findImage";
    public override string DisplayName => "Find Image (Android)";
    public override string Description => "Finds a template image on the device screen and writes its location to variables.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TemplateMatchCore.TemplatePathField(),
        TemplateMatchCore.ConfidenceField(),
        TemplateMatchCore.ResultVarField(),
    ];

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var templatePath = ConfigValues.GetString(context.Action.Config, TemplateMatchCore.TemplatePathKey);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return Task.FromResult(ActionResult.Fail("Find Image (Android): a template image path is required."));
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, TemplateMatchCore.ConfidenceKey, TemplateMatchCore.DefaultConfidence);
        var prefix = ConfigValues.GetString(context.Action.Config, TemplateMatchCore.ResultVarKey, TemplateMatchCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = TemplateMatchCore.DefaultResultVar;
        }

        if (CaptureAndMatch(context, device, _matcher, templatePath, confidence) is not MatchResult m)
        {
            return Task.FromResult(ActionResult.Fail("Find Image (Android): no match at or above the configured confidence."));
        }

        TemplateMatchCore.WriteMatchVariables(context.Context.Variables, m, prefix, _random);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
