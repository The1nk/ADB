using System.Globalization;

namespace AdbCore.Input;

/// <summary>Resolves friendly key names (e.g. "Enter", "F5", "A", "Up") to Win32 virtual-key codes.
/// Case-insensitive. Single letters A–Z and digits 0–9 map directly; named keys via a table.</summary>
public static class VirtualKeys
{
    private static readonly Dictionary<string, ushort> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D, ["Return"] = 0x0D,
        ["Esc"] = 0x1B, ["Escape"] = 0x1B,
        ["Tab"] = 0x09, ["Space"] = 0x20, ["Backspace"] = 0x08,
        ["Delete"] = 0x2E, ["Del"] = 0x2E, ["Insert"] = 0x2D,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
    };

    /// <summary>Resolves a key name to its virtual-key code. Returns false (and vk=0) if unrecognized.</summary>
    public static bool TryResolve(string keyName, out ushort virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return false;
        }

        var key = keyName.Trim();

        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = (ushort)c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                virtualKey = (ushort)c;
                return true;
            }
        }

        if (Named.TryGetValue(key, out var named))
        {
            virtualKey = named;
            return true;
        }

        // Function keys F1–F12 -> 0x70–0x7B
        if ((key[0] is 'F' or 'f') && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 12)
        {
            virtualKey = (ushort)(0x70 + (n - 1));
            return true;
        }

        return false;
    }
}
