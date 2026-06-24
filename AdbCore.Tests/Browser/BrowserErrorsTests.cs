using AdbCore.Browser;
using Microsoft.Playwright;
using Xunit;

namespace AdbCore.Tests.Browser;

public class BrowserErrorsTests
{
    [Fact]
    public void Translate_ClosedPagePlaywrightException_ReturnsActionableMessage()
    {
        // Simulate the kind of PlaywrightException Playwright throws when the page is closed.
        var ex = new PlaywrightException("Target page, context or browser has been closed");

        var msg = BrowserErrors.Translate(ex);

        Assert.NotNull(msg);
        Assert.Contains("browser page has been closed", msg);
        Assert.Contains("re-run the bot", msg);
    }

    [Fact]
    public void Translate_PlaywrightException_OtherMessage_ReturnsNull()
    {
        // A selector-timeout exception must not be remapped — it has a different, actionable message already.
        var ex = new PlaywrightException("Timeout 30000ms exceeded.");

        var msg = BrowserErrors.Translate(ex);

        Assert.Null(msg);
    }

    [Fact]
    public void Translate_NonPlaywrightException_ReturnsNull()
    {
        var ex = new InvalidOperationException("Some other error");

        var msg = BrowserErrors.Translate(ex);

        Assert.Null(msg);
    }

    [Fact]
    public void Translate_ClosedPageMessage_CaseInsensitive()
    {
        // Playwright may vary casing; the check must be case-insensitive.
        var ex = new PlaywrightException("Target Page, Context Or Browser Has Been Closed");

        var msg = BrowserErrors.Translate(ex);

        Assert.NotNull(msg);
    }

    [Fact]
    public void Translate_TargetClosedPhrasing_ReturnsActionableMessage()
    {
        // Some Playwright versions emit the shorter "Target closed" form.
        var ex = new PlaywrightException("Target closed");

        var msg = BrowserErrors.Translate(ex);

        Assert.NotNull(msg);
        Assert.Contains("browser page has been closed", msg);
    }
}
