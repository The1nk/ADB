namespace AdbCore.Browser;

/// <summary>A live browser page, bound to a Playwright-launched browser. Stored as the
/// <c>ResolvedTarget.Handle</c> for Browser targets; the Browser actions await it.</summary>
public interface IBrowserPage
{
    Task GotoAsync(string url);
    Task ClickAsync(string selector);

    /// <summary>Sets the value of the element matched by <paramref name="selector"/>.</summary>
    Task TypeAsync(string selector, string text);

    Task WaitForSelectorAsync(string selector, int timeoutMs);

    /// <summary>The visible text of the element matched by <paramref name="selector"/>.</summary>
    Task<string> GetTextAsync(string selector);
}
