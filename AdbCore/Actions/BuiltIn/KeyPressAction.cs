using AdbCore.Execution;
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Presses a configured key (by name) with optional Ctrl/Alt/Shift/Win modifiers.</summary>
public sealed class KeyPressAction : InputActionBase
{
    public const string KeyKey = "key";
    public const string CtrlKey = "ctrl";
    public const string AltKey = "alt";
    public const string ShiftKey = "shift";
    public const string WinKey = "win";

    public KeyPressAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.keyPress";
    public override string DisplayName => "Key Press";
    public override string Description => "Presses a key, with optional Ctrl/Alt/Shift/Win modifiers.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        new ConfigField { Key = KeyKey, Label = "Key", Type = ConfigFieldType.String },
        new ConfigField { Key = CtrlKey, Label = "Ctrl", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = AltKey, Label = "Alt", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = ShiftKey, Label = "Shift", Type = ConfigFieldType.Boolean, DefaultValue = false },
        new ConfigField { Key = WinKey, Label = "Win", Type = ConfigFieldType.Boolean, DefaultValue = false },
    ];

    protected override Task<ActionResult> PerformAsync(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context, CancellationToken ct)
    {
        var keyName = ConfigValues.GetString(context.Action.Config, KeyKey);
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return Task.FromResult(ActionResult.Fail("Key Press: a key is required."));
        }

        if (!VirtualKeys.TryResolve(keyName, out var vk))
        {
            return Task.FromResult(ActionResult.Fail($"Key Press: unrecognized key '{keyName}'."));
        }

        var modifiers = KeyModifiers.None;
        if (ConfigValues.GetBool(context.Action.Config, CtrlKey)) modifiers |= KeyModifiers.Control;
        if (ConfigValues.GetBool(context.Action.Config, AltKey)) modifiers |= KeyModifiers.Alt;
        if (ConfigValues.GetBool(context.Action.Config, ShiftKey)) modifiers |= KeyModifiers.Shift;
        if (ConfigValues.GetBool(context.Action.Config, WinKey)) modifiers |= KeyModifiers.Win;

        sender.KeyPress(windowHandle, vk, modifiers);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
