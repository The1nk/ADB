namespace AdbCore.Input;

/// <summary>Modifier keys that may be held while a Key Press fires.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}
