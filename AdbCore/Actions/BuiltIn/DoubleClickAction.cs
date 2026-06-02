using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Performs a left double-click at client-relative coordinates of the action's Window target.</summary>
public sealed class DoubleClickAction : PointerActionBase
{
    public DoubleClickAction(InputSenderResolver senders) : base(senders)
    {
    }

    public override string TypeKey => "input.doubleClick";
    public override string DisplayName => "Double Click";
    public override string Description => "Double-clicks at coordinates within the target window.";

    protected override void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y)
        => sender.DoubleClick(windowHandle, x, y);
}
