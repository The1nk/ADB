namespace AdbCore.Android;

/// <summary>Resolves friendly key names (e.g. "Backspace", "Enter", "Up") to Android <c>KEYCODE_*</c>
/// values for <c>input keyevent</c>. Distinct from <see cref="AdbCore.Input.VirtualKeys"/>, whose codes
/// are Win32 virtual-key codes and do not match Android. Single source of truth for the Press Key
/// action's dropdown options and its name→code resolution.</summary>
public static class AndroidKeyCodes
{
    // Ordered so the dropdown lists the most-used keys first (Backspace clears a field).
    private static readonly (string Name, int Code)[] Entries =
    [
        ("Backspace", 67),      // KEYCODE_DEL
        ("Delete (Fwd)", 112),  // KEYCODE_FORWARD_DEL
        ("Enter", 66),          // KEYCODE_ENTER
        ("Tab", 61),            // KEYCODE_TAB
        ("Space", 62),          // KEYCODE_SPACE
        ("Up", 19),             // KEYCODE_DPAD_UP
        ("Down", 20),           // KEYCODE_DPAD_DOWN
        ("Left", 21),           // KEYCODE_DPAD_LEFT
        ("Right", 22),          // KEYCODE_DPAD_RIGHT
        ("Home", 122),          // KEYCODE_MOVE_HOME
        ("End", 123),           // KEYCODE_MOVE_END
        ("Escape", 111),        // KEYCODE_ESCAPE
    ];

    private static readonly Dictionary<string, int> ByName =
        Entries.ToDictionary(e => e.Name, e => e.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>Display names in dropdown order; used verbatim as the Press Key <c>key</c> field options.</summary>
    public static IReadOnlyList<string> Names { get; } = Entries.Select(e => e.Name).ToArray();

    /// <summary>Resolves a key name to its Android keycode. Case-insensitive; false for unknown/blank.</summary>
    public static bool TryResolve(string name, out int keyCode)
    {
        keyCode = 0;
        return !string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name.Trim(), out keyCode);
    }
}
