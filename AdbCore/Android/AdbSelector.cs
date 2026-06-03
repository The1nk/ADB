namespace AdbCore.Android;

/// <summary>Parses an Android device target selector of the form <c>serial:&lt;device&gt;</c>.</summary>
public static class AdbSelector
{
    private const string Prefix = "serial:";

    /// <summary>The device serial from a <c>serial:&lt;device&gt;</c> selector, or null when the selector
    /// isn't a (non-empty) serial selector.</summary>
    public static string? ParseSerial(string selector)
        => selector.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && selector.Length > Prefix.Length
            ? selector[Prefix.Length..]
            : null;
}
