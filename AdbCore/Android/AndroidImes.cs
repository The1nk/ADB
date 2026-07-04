namespace AdbCore.Android;

/// <summary>Known Android input-method (IME) component ids. Single source of truth so the Enable
/// action and the Send Text ADB-Keyboard guard reference the same string.</summary>
public static class AndroidImes
{
    /// <summary>The ADBKeyboard IME component id — the Unicode-capable keyboard used by Send Text's
    /// "ADB Keyboard" method (github.com/senzhk/ADBKeyBoard).</summary>
    public const string AdbKeyboard = "com.android.adbkeyboard/.AdbIME";
}
