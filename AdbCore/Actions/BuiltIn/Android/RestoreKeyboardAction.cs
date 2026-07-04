using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Restores the input method saved by the Enable ADB Keyboard node, reading the IME id from
/// the named run variable.</summary>
public sealed class RestoreKeyboardAction : AndroidActionBase
{
    public const string PreviousImeVarKey = "previousImeVar";
    public const string DefaultPreviousImeVar = "PreviousIme";

    public override string TypeKey => "android.restoreKeyboard";
    public override string DisplayName => "Restore Keyboard";
    public override string Description => "Restores the input method saved by Enable ADB Keyboard.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = PreviousImeVarKey,
            Label = "Previous IME Variable",
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

        var varName = ConfigValues.GetString(context.Action.Config, PreviousImeVarKey, DefaultPreviousImeVar);
        context.Context.Variables.TryGetValue(varName, out var stored);
        var ime = stored as string ?? stored?.ToString();
        if (string.IsNullOrWhiteSpace(ime))
        {
            return Task.FromResult(ActionResult.Fail(
                $"No previous IME recorded in '{varName}' — run 'Enable ADB Keyboard' first."));
        }

        device.SetInputMethod(ime);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
