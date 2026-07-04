using System.Globalization;

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

    private static string SingleQuote(string text)
        => "'" + text.Replace("'", "'\\''") + "'";
}
