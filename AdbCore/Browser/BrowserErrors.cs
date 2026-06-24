using Microsoft.Playwright;

namespace AdbCore.Browser;

/// <summary>Maps Playwright exceptions to clear, user-facing error messages. Purely static; unit-testable
/// without launching Playwright.</summary>
public static class BrowserErrors
{
    /// <summary>Closed-page sentinel — the observed Playwright .NET exception message when a page, context,
    /// or browser was disposed before the operation completed (e.g. "Target page, context or browser has been closed").</summary>
    internal const string ClosedPageSubstring = "has been closed";

    /// <summary>Shorter closed-target phrasing emitted by some Playwright versions (e.g. "Target closed").</summary>
    internal const string TargetClosedSubstring = "Target closed";

    /// <summary>If <paramref name="ex"/> indicates that the Playwright target page/context/browser has been
    /// closed, returns a clear actionable message. Returns <see langword="null"/> for all other exceptions
    /// (e.g. selector timeouts), which the caller should re-throw as-is.</summary>
    public static string? Translate(Exception ex)
    {
        if (ex is PlaywrightException &&
            (ex.Message.Contains(ClosedPageSubstring, StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains(TargetClosedSubstring, StringComparison.OrdinalIgnoreCase)))
        {
            return "The browser page has been closed (it may have been closed manually or crashed); re-run the bot to relaunch the browser.";
        }

        return null;
    }
}
