using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Moves the pointer to client-relative coordinates of the action's Window target (no click).</summary>
public sealed class MouseMoveAction : PointerActionBase
{
    public MouseMoveAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.mouseMove";
    public override string DisplayName => "Mouse Move";
    public override string Description => "Moves the pointer to coordinates within the target window.";

    protected override void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y)
        => sender.MoveTo(windowHandle, x, y);
}
