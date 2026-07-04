using AdbCore.Android;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Makes the ADBKeyboard IME active (for Unicode Send Text) and stashes the previously-active
/// IME id into a run variable so a later Restore Keyboard node can put it back.</summary>
public sealed class EnableAdbKeyboardAction : AndroidActionBase
{
    public const string PreviousImeVarKey = "previousImeVar";
    public const string DefaultPreviousImeVar = "PreviousIme";

    public override string TypeKey => "android.enableAdbKeyboard";
    public override string DisplayName => "Enable ADB Keyboard";
    public override string Description => "Activates the ADBKeyboard IME (for Unicode text) and remembers the previous keyboard.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = PreviousImeVarKey,
            Label = "Remember Previous IME In",
            Type = ConfigFieldType.String,
            DefaultValue = DefaultPreviousImeVar,
        },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        if (!device.IsInputMethodAvailable(AndroidImes.AdbKeyboard))
        {
            return Task.FromResult(ActionResult.Fail(
                "ADBKeyboard is not installed on the device. Install the ADBKeyboard APK (e.g. with the Install APK action) and try again."));
        }

        var previous = device.GetInputMethod();
        device.EnableInputMethod(AndroidImes.AdbKeyboard);
        device.SetInputMethod(AndroidImes.AdbKeyboard);

        var varName = ConfigValues.GetString(context.Action.Config, PreviousImeVarKey, DefaultPreviousImeVar);
        if (!string.IsNullOrWhiteSpace(varName))
        {
            context.Context.Variables[varName] = previous;
        }

        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
