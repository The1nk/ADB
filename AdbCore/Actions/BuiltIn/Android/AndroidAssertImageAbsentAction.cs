using System.Globalization;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Succeeds only when the template is NOT found on the device screen. While the template is
/// present it returns a failed result, so with a RetryPolicy it becomes "wait until the image is gone",
/// and otherwise routes onFailure.</summary>
public sealed class AndroidAssertImageAbsentAction : AndroidImageActionBase
{
    private readonly ITemplateMatcher _matcher;

    public AndroidAssertImageAbsentAction(ITemplateMatcher matcher)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        _matcher = matcher;
    }

    public override string TypeKey => "android.assertImageAbsent";
    public override string DisplayName => "Assert Image Absent (Android)";
    public override string Description => "Succeeds when the template is not present on the device screen.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TemplateMatchCore.TemplatePathField(),
        TemplateMatchCore.ConfidenceField(),
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
            return Task.FromResult(ActionResult.Fail("Assert Image Absent (Android): a template image path is required."));
        }

        var confidence = ConfigValues.GetDouble(context.Action.Config, TemplateMatchCore.ConfidenceKey, TemplateMatchCore.DefaultConfidence);

        return CaptureAndMatch(context, device, _matcher, templatePath, confidence) is MatchResult m
            ? Task.FromResult(ActionResult.Fail($"Assert Image Absent (Android): template is present (score {m.Score.ToString(CultureInfo.InvariantCulture)})."))
            : Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
