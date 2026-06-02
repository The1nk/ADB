using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Performs a right click at client-relative coordinates of the action's Window target.</summary>
public sealed class RightClickAction : PointerActionBase
{
    public RightClickAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.rightClick";
    public override string DisplayName => "Right Click";
    public override string Description => "Right-clicks at coordinates within the target window.";

    protected override void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y)
        => sender.RightClick(windowHandle, x, y);
}
