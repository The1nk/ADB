using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Swipes from (x1, y1) to (x2, y2) over a duration.</summary>
public sealed class SwipeAction : AndroidActionBase
{
    public override string TypeKey => "android.swipe";
    public override string DisplayName => "Swipe";
    public override string Description => "Swipes between two points over the given duration.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "x1", Label = "From X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "y1", Label = "From Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "x2", Label = "To X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "y2", Label = "To Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = "durationMs", Label = "Duration (ms)", Type = ConfigFieldType.Number, DefaultValue = 300 },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var c = context.Action.Config;
        device.Swipe(
            ConfigValues.GetInt(c, "x1"), ConfigValues.GetInt(c, "y1"),
            ConfigValues.GetInt(c, "x2"), ConfigValues.GetInt(c, "y2"),
            ConfigValues.GetInt(c, "durationMs", 300));
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
