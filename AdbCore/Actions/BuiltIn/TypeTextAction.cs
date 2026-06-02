using AdbCore.Execution;
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Types a configured string into the target window.</summary>
public sealed class TypeTextAction : InputActionBase
{
    public const string TextKey = "text";

    public TypeTextAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.typeText";
    public override string DisplayName => "Type Text";
    public override string Description => "Types text into the target window.";

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        new ConfigField { Key = TextKey, Label = "Text", Type = ConfigFieldType.MultilineString },
    ];

    protected override ActionResult Perform(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context)
    {
        var text = ConfigValues.GetString(context.Action.Config, TextKey);
        sender.TypeText(windowHandle, text);
        return ActionResult.Ok(SuccessPort);
    }
}
