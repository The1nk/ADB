using AdbCore.Android;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Sends a named Android key (Backspace, Enter, arrows, …) one or more times — e.g. Backspace
/// with a high count clears the focused field.</summary>
public sealed class PressKeyAction : AndroidActionBase
{
    public override string TypeKey => "android.pressKey";
    public override string DisplayName => "Press Key";
    public override string Description => "Sends a named key to the device, optionally repeated.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = "key",
            Label = "Key",
            Type = ConfigFieldType.Enum,
            DefaultValue = "Backspace",
            Options = AndroidKeyCodes.Names.ToList(),
        },
        new ConfigField { Key = "count", Label = "Count", Type = ConfigFieldType.Number, DefaultValue = 1 },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var c = context.Action.Config;
        var keyName = ConfigValues.GetString(c, "key", "Backspace");
        if (!AndroidKeyCodes.TryResolve(keyName, out var keyCode))
        {
            return Task.FromResult(ActionResult.Fail($"Press Key: unrecognized key '{keyName}'."));
        }

        var count = Math.Max(1, ConfigValues.GetInt(c, "count", 1));
        device.KeyEvent(keyCode, count);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
