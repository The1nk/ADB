using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Performs a left click at client-relative coordinates of the action's Window target.</summary>
public sealed class ClickAction : PointerActionBase
{
    public ClickAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.click";
    public override string DisplayName => "Click";
    public override string Description => "Clicks at coordinates within the target window.";

    protected override void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y)
        => sender.Click(windowHandle, x, y);
}
