namespace AdbCore.Targets;

/// <summary>The kind of window a selector identifies.</summary>
public enum WindowSelectorKind
{
    Process,
    Title,
    Handle,
}

/// <summary>A parsed Window target selector, e.g. <c>process:Notepad</c>, <c>title:Untitled</c>, <c>hwnd:0x1A2B</c>.</summary>
public readonly record struct WindowSelector(WindowSelectorKind Kind, string Value)
{
    /// <summary>Parses a <c>kind:value</c> selector. The value keeps everything after the first colon
    /// verbatim (so titles may contain colons). Throws <see cref="FormatException"/> for an unknown
    /// prefix, a missing colon, or an empty value.</summary>
    public static WindowSelector Parse(string selector)
    {
        var colon = selector.IndexOf(':');
        if (colon <= 0 || colon == selector.Length - 1)
        {
            throw new FormatException($"Invalid window selector '{selector}'. Expected 'process:|title:|hwnd:<value>'.");
        }

        var prefix = selector[..colon];
        var value = selector[(colon + 1)..];

        var kind = prefix.ToLowerInvariant() switch
        {
            "process" => WindowSelectorKind.Process,
            "title" => WindowSelectorKind.Title,
            "hwnd" => WindowSelectorKind.Handle,
            _ => throw new FormatException($"Unknown window selector prefix '{prefix}'. Use process:, title:, or hwnd:."),
        };

        return new WindowSelector(kind, value);
    }
}
