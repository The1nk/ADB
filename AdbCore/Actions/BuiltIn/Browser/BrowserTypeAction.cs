using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Types text into the element matched by a selector.</summary>
public sealed class BrowserTypeAction : BrowserActionBase
{
    public override string TypeKey => "browser.type";
    public override string DisplayName => "Type";
    public override string Description => "Sets the value of the element matched by a selector.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "selector", Label = "Selector", Type = ConfigFieldType.String, DefaultValue = "" },
        new ConfigField { Key = "text", Label = "Text", Type = ConfigFieldType.String, DefaultValue = "" },
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
            return ActionResult.Fail("Type: a selector is required.");
        }

        await page.TypeAsync(selector, ConfigValues.GetString(context.Action.Config, "text"));
        return ActionResult.Ok(SuccessPort);
    }
}
