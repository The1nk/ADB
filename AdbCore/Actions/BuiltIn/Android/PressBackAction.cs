using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Presses the Android Back button.</summary>
public sealed class PressBackAction : AndroidActionBase
{
    public override string TypeKey => "android.pressBack";
    public override string DisplayName => "Press Back";
    public override string Description => "Sends the Back key to the device.";
    public override List<ConfigField> ConfigFields { get; } = new();

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        device.PressBack();
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
