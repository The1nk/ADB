namespace AdbCore.Input;

/// <summary>Sends synthetic input to a target window, addressed by its HWND with client-relative
/// coordinates. Implementations choose the delivery mechanism (foreground SendInput, or background
/// PostMessage) — see the concrete senders for their trade-offs.</summary>
public interface IInputSender
{
    /// <summary>Delivers a left click at the given client-relative coordinates of <paramref name="windowHandle"/>.</summary>
    void Click(IntPtr windowHandle, int clientX, int clientY);
}
