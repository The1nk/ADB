namespace AdbCore.Browser;

/// <summary>Parses a Browser target selector of the form <c>browser:&lt;engine&gt;</c> (chromium / firefox /
/// webkit). Playwright launches its own browser, so the selector names the engine (not an external context).</summary>
public static class BrowserSelector
{
    private const string Prefix = "browser:";

    /// <summary>The Playwright engines the picker offers.</summary>
    public static IReadOnlyList<string> Engines { get; } = new[] { "chromium", "firefox", "webkit" };

    /// <summary>The engine from a <c>browser:&lt;engine&gt;</c> selector (empty engine -> chromium), or null
    /// when the selector isn't a browser selector.</summary>
    public static string? ParseEngine(string selector)
    {
        if (!selector.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var engine = selector[Prefix.Length..].Trim().ToLowerInvariant();
        return engine.Length == 0 ? "chromium" : engine;
    }
}
