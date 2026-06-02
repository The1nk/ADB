using AdbCore.Execution;
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Input pointer actions (Click / Right Click / Double Click / Mouse Move):
/// adds the X/Y coordinate fields and dispatches the specific pointer operation to the sender.</summary>
public abstract class PointerActionBase : InputActionBase
{
    public const string XKey = "x";
    public const string YKey = "y";

    protected PointerActionBase(InputSenderResolver senders) : base(senders)
    {
    }

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        new ConfigField { Key = XKey, Label = "X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = YKey, Label = "Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
    ];

    /// <summary>Dispatches the specific pointer operation (click/right-click/double-click/move) to the sender.</summary>
    protected abstract void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y);

    protected override Task<ActionResult> PerformAsync(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context, CancellationToken ct)
    {
        var x = ConfigValues.GetInt(context.Action.Config, XKey);
        var y = ConfigValues.GetInt(context.Action.Config, YKey);
        Dispatch(sender, windowHandle, x, y);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
