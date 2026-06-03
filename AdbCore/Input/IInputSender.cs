namespace AdbCore.Input;

/// <summary>Sends synthetic input to a target window, addressed by its HWND with client-relative
/// coordinates. Implementations choose the delivery mechanism (foreground SendInput, or background
/// PostMessage) — see the concrete senders for their trade-offs.</summary>
public interface IInputSender
{
    /// <summary>Delivers a left click at the given client-relative coordinates of <paramref name="windowHandle"/>.</summary>
    void Click(IntPtr windowHandle, int clientX, int clientY);

    /// <summary>Delivers a right click at the given client-relative coordinates of <paramref name="windowHandle"/>.</summary>
    void RightClick(IntPtr windowHandle, int clientX, int clientY);

    /// <summary>Delivers a left double-click at the given client-relative coordinates of <paramref name="windowHandle"/>.</summary>
    void DoubleClick(IntPtr windowHandle, int clientX, int clientY);

    /// <summary>Moves the pointer to the given client-relative coordinates of <paramref name="windowHandle"/> (no button press).</summary>
    void MoveTo(IntPtr windowHandle, int clientX, int clientY);

    /// <summary>Types the given text into <paramref name="windowHandle"/>, pausing <paramref name="keyDelayMs"/>
    /// ms after each synthetic key event so fast targets don't drop or auto-repeat keys.</summary>
    Task TypeText(IntPtr windowHandle, string text, int keyDelayMs, CancellationToken ct);

    /// <summary>Presses <paramref name="virtualKey"/> while holding <paramref name="modifiers"/>, then releases,
    /// pausing <paramref name="keyDelayMs"/> ms after each synthetic key event.</summary>
    Task KeyPress(IntPtr windowHandle, ushort virtualKey, KeyModifiers modifiers, int keyDelayMs, CancellationToken ct);
}
