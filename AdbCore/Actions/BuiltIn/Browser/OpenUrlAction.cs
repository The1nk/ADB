using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Navigates the browser to a URL.</summary>
public sealed class OpenUrlAction : BrowserActionBase
{
    public override string TypeKey => "browser.openUrl";
    public override string DisplayName => "Open URL";
    public override string Description => "Navigates the browser page to a URL.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "url", Label = "URL", Type = ConfigFieldType.String, DefaultValue = "" },
    };

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolvePage(context) is not { } page)
        {
            return RequiresPage();
        }

        var url = ConfigValues.GetString(context.Action.Config, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            return ActionResult.Fail("Open URL: a URL is required.");
        }

        await page.GotoAsync(url);
        return ActionResult.Ok(SuccessPort);
    }
}
