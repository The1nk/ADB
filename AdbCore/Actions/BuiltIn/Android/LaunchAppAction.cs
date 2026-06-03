using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Launches an app by package name.</summary>
public sealed class LaunchAppAction : AndroidActionBase
{
    public override string TypeKey => "android.launchApp";
    public override string DisplayName => "Launch App";
    public override string Description => "Launches an installed app by its package name.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "package", Label = "Package Name", Type = ConfigFieldType.String, DefaultValue = "" },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var package = ConfigValues.GetString(context.Action.Config, "package");
        if (string.IsNullOrWhiteSpace(package))
        {
            return Task.FromResult(ActionResult.Fail("Launch App: a package name is required."));
        }

        device.LaunchApp(package);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
