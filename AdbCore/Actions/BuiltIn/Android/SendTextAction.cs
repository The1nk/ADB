using AdbCore.Android;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Types text into the focused field. "Input Text" uses <c>input text</c> (ASCII only);
/// "ADB Keyboard" broadcasts base64 to the ADBKeyboard IME for full Unicode. Empty text is a no-op.</summary>
public sealed class SendTextAction : AndroidActionBase
{
    public const string MethodInputText = "Input Text";
    public const string MethodAdbKeyboard = "ADB Keyboard";

    public override string TypeKey => "android.sendText";
    public override string DisplayName => "Send Text";
    public override string Description => "Types text into the focused field on the device.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "text", Label = "Text", Type = ConfigFieldType.MultilineString },
        new ConfigField
        {
            Key = "method",
            Label = "Method",
            Type = ConfigFieldType.Enum,
            DefaultValue = MethodInputText,
            Options = new List<string> { MethodInputText, MethodAdbKeyboard },
        },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var text = ConfigValues.GetString(context.Action.Config, "text");
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(ActionResult.Ok(SuccessPort)); // empty is a no-op, either method
        }

        var method = ConfigValues.GetString(context.Action.Config, "method", MethodInputText);
        if (string.Equals(method, MethodAdbKeyboard, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(device.GetInputMethod(), AndroidImes.AdbKeyboard, StringComparison.Ordinal))
            {
                return Task.FromResult(ActionResult.Fail(
                    "Send Text (ADB Keyboard) requires an 'Enable ADB Keyboard' node earlier in the bot — ADBKeyboard is not the active input method."));
            }

            device.SendAdbKeyboardText(text);
        }
        else
        {
            device.SendText(text);
        }

        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
