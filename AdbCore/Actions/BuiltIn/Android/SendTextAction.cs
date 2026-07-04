using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Types literal text into the focused field on the device. Empty text is a no-op.</summary>
public sealed class SendTextAction : AndroidActionBase
{
    public override string TypeKey => "android.sendText";
    public override string DisplayName => "Send Text";
    public override string Description => "Types text into the focused field on the device.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "text", Label = "Text", Type = ConfigFieldType.MultilineString },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var text = ConfigValues.GetString(context.Action.Config, "text");
        if (!string.IsNullOrEmpty(text))
        {
            device.SendText(text);
        }
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
