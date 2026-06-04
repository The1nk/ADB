using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Clicks the element matched by a selector.</summary>
public sealed class BrowserClickAction : BrowserActionBase
{
    public override string TypeKey => "browser.click";
    public override string DisplayName => "Click Element";
    public override string Description => "Clicks the element matched by a CSS/text selector.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "selector", Label = "Selector", Type = ConfigFieldType.String, DefaultValue = "" },
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
            return ActionResult.Fail("Click Element: a selector is required.");
        }

        await page.ClickAsync(selector);
        return ActionResult.Ok(SuccessPort);
    }
}
