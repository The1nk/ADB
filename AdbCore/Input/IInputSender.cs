namespace AdbCore.Input;

/// <summary>Sends synthetic input to a target window, addressed by its HWND with client-relative
/// coordinates. The Win32 implementation uses PostMessage so input can target a window without it
/// being foreground. (Run-time only; not all windows honour synthesized messages.)</summary>
public interface IInputSender
{
    /// <summary>Posts a left click at the given client-relative coordinates of <paramref name="windowHandle"/>.</summary>
    void Click(IntPtr windowHandle, int clientX, int clientY);
}
