using Microsoft.Playwright;

namespace AdbCore.Browser;

/// <summary>An <see cref="IBrowserPage"/> backed by a Playwright-launched browser. Launch-owned: it starts
/// its own browser engine and closes it on dispose. Verified live (requires installed browsers —
/// `playwright install`).</summary>
public sealed class PlaywrightBrowserPage : IBrowserPage, IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IPage _page;

    private PlaywrightBrowserPage(IPlaywright playwright, IBrowser browser, IPage page)
    {
        _playwright = playwright;
        _browser = browser;
        _page = page;
    }

    /// <summary>Launches the given engine (chromium / firefox / webkit) and opens a page.</summary>
    public static async Task<PlaywrightBrowserPage> LaunchAsync(string engine, bool headless)
    {
        var playwright = await Playwright.CreateAsync();
        var browserType = playwright[engine]; // indexer: "chromium" / "firefox" / "webkit"
        var browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions { Headless = headless });
        var page = await browser.NewPageAsync();
        return new PlaywrightBrowserPage(playwright, browser, page);
    }

    public Task GotoAsync(string url) => _page.GotoAsync(url);                       // Task<IResponse?> is-a Task
    public Task ClickAsync(string selector) => _page.ClickAsync(selector);
    public Task TypeAsync(string selector, string text) => _page.FillAsync(selector, text);
    public Task WaitForSelectorAsync(string selector, int timeoutMs)
        => _page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = timeoutMs });
    public Task<string> GetTextAsync(string selector) => _page.InnerTextAsync(selector);

    public async ValueTask DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }
}
