using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Taps the Android screen at (x, y).</summary>
public sealed class TapAction : AndroidActionBase
{
    public override string TypeKey => "android.tap";
    public override string DisplayName => "Tap";
    public override string Description => "Taps the device screen at the given coordinates.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "x", Label = "X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "y", Label = "Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        device.Tap(
            ConfigValues.GetInt(context.Action.Config, "x"),
            ConfigValues.GetInt(context.Action.Config, "y"));
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
