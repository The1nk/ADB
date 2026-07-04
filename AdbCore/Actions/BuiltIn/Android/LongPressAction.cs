using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Presses and holds the Android screen at (x, y) for a duration.</summary>
public sealed class LongPressAction : AndroidActionBase
{
    public override string TypeKey => "android.longPress";
    public override string DisplayName => "Long Press";
    public override string Description => "Presses and holds the device screen at the given coordinates.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "x", Label = "X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "y", Label = "Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "durationMs", Label = "Duration (ms)", Type = ConfigFieldType.Number, DefaultValue = 600 },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var c = context.Action.Config;
        device.LongPress(
            ConfigValues.GetInt(c, "x"),
            ConfigValues.GetInt(c, "y"),
            ConfigValues.GetInt(c, "durationMs", 600));
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
