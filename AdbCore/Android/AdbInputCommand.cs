using System.Globalization;
using System.Text;

namespace AdbCore.Android;

/// <summary>Builds <c>input …</c> device-shell command strings for gesture/text/key actions. Kept
/// separate from the device so the string construction — especially <c>input text</c> quoting — is
/// unit-testable without a live ADB connection.</summary>
public static class AdbInputCommand
{
    /// <summary>A press-and-hold: a swipe whose start and end are the same point, held for
    /// <paramref name="durationMs"/> — the standard adb long-press idiom.</summary>
    public static string LongPress(int x, int y, int durationMs)
        => $"input swipe {x} {y} {x} {y} {durationMs}";

    /// <summary>Types literal text. The argument is single-quote-wrapped (embedded quotes escaped as
    /// <c>'\''</c>) so spaces and shell metacharacters are passed literally to <c>input text</c>.</summary>
    public static string Text(string text)
        => $"input text {SingleQuote(text ?? string.Empty)}";

    /// <summary>Sends <c>input keyevent</c> with the keycode repeated <paramref name="count"/> times in a
    /// single invocation (min 1), so "Backspace × N" is one shell round-trip.</summary>
    public static string KeyEvent(int keyCode, int count)
    {
        var n = Math.Max(1, count);
        var code = keyCode.ToString(CultureInfo.InvariantCulture);
        return "input keyevent " + string.Join(' ', Enumerable.Repeat(code, n));
    }

    /// <summary>Broadcasts text to the ADBKeyboard IME as base64-encoded UTF-8 (<c>ADB_INPUT_B64</c>), so
    /// arbitrary Unicode passes through without any shell-quoting or encoding hazard. Requires the
    /// ADBKeyboard IME to be installed and active on the device.</summary>
    public static string AdbKeyboardText(string text)
        => $"am broadcast -a ADB_INPUT_B64 --es msg {SingleQuote(Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? string.Empty)))}";

    /// <summary>Makes <paramref name="ime"/> the active input method (<c>ime set</c>).</summary>
    public static string SetIme(string ime) => $"ime set {ime}";

    /// <summary>Enables <paramref name="ime"/> so it can be activated (<c>ime enable</c>).</summary>
    public static string EnableIme(string ime) => $"ime enable {ime}";

    /// <summary>Reads the currently-active IME id from secure settings.</summary>
    public static string GetDefaultIme() => "settings get secure default_input_method";

    /// <summary>Lists all installed IME ids, one per line (<c>-a</c> all, <c>-s</c> ids only).</summary>
    public static string ListImes() => "ime list -a -s";

    private static string SingleQuote(string text)
        => "'" + text.Replace("'", "'\\''") + "'";
}
