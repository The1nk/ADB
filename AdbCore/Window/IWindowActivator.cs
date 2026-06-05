namespace AdbCore.Window;

/// <summary>Brings a window to the foreground. Injectable so the Activate Window action is unit-testable
/// without a real window.</summary>
public interface IWindowActivator
{
    /// <summary>Restores the window if minimized and brings it to the foreground.</summary>
    void Activate(IntPtr handle);
}
