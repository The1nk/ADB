using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Reads the text of an element into a run variable.</summary>
public sealed class GetTextAction : BrowserActionBase
{
    public const string DefaultResultVar = "text";

    public override string TypeKey => "browser.getText";
    public override string DisplayName => "Get Text";
    public override string Description => "Reads the visible text of an element into a variable.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "selector", Label = "Selector", Type = ConfigFieldType.String, DefaultValue = "" },
        new ConfigField { Key = "resultVar", Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar },
    };

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolvePage(context) is not { } page)
        {
            return RequiresPage();
        }

        var selector = ConfigValues.GetString(context.Action.Config, "selector");
        if (string.IsNullOrWhiteSpace(selector))
        {
            return ActionResult.Fail("Get Text: a selector is required.");
        }

        var resultVar = ConfigValues.GetString(context.Action.Config, "resultVar");
        if (string.IsNullOrWhiteSpace(resultVar))
        {
            resultVar = DefaultResultVar;
        }

        context.Context.Variables[resultVar] = await page.GetTextAsync(selector);
        return ActionResult.Ok(SuccessPort);
    }
}
