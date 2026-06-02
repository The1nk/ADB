namespace AdbCore.Input;

/// <summary>Sends synthetic input to a target window, addressed by its HWND with client-relative
/// coordinates. Implementations choose the delivery mechanism (foreground SendInput, or background
/// PostMessage) — see the concrete senders for their trade-offs.</summary>
public interface IInputSender
{
    /// <summary>Delivers a left click at the given client-relative coordinates of <paramref name="windowHandle"/>.</summary>
    void Click(IntPtr windowHandle, int clientX, int clientY);

    /// <summary>Delivers a right click at the given client-relative coordinates.</summary>
    void RightClick(IntPtr windowHandle, int clientX, int clientY);

    /// <summary>Delivers a left double-click at the given client-relative coordinates.</summary>
    void DoubleClick(IntPtr windowHandle, int clientX, int clientY);

    /// <summary>Moves the pointer to the given client-relative coordinates (no button press).</summary>
    void MoveTo(IntPtr windowHandle, int clientX, int clientY);
}
