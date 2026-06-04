using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Waits for an element matching a selector to appear.</summary>
public sealed class WaitForSelectorAction : BrowserActionBase
{
    public override string TypeKey => "browser.waitForSelector";
    public override string DisplayName => "Wait for Selector";
    public override string Description => "Waits up to a timeout for an element to appear.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "selector", Label = "Selector", Type = ConfigFieldType.String, DefaultValue = "" },
        new ConfigField { Key = "timeoutMs", Label = "Timeout (ms)", Type = ConfigFieldType.Number, DefaultValue = 30000 },
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
            return ActionResult.Fail("Wait for Selector: a selector is required.");
        }

        await page.WaitForSelectorAsync(selector, ConfigValues.GetInt(context.Action.Config, "timeoutMs", 30000));
        return ActionResult.Ok(SuccessPort);
    }
}
