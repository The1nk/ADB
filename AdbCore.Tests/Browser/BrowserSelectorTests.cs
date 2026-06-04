using AdbCore.Browser;

namespace AdbCore.Tests.Browser;

public class BrowserSelectorTests
{
    [Theory]
    [InlineData("browser:firefox", "firefox")]
    [InlineData("browser:WEBKIT", "webkit")]
    [InlineData("browser:", "chromium")]      // no engine -> default chromium
    public void ParseEngine_ReturnsEngine(string selector, string expected)
        => Assert.Equal(expected, BrowserSelector.ParseEngine(selector));

    [Theory]
    [InlineData("url:https://x")]
    [InlineData("chromium")]
    public void ParseEngine_NonBrowser_ReturnsNull(string selector)
        => Assert.Null(BrowserSelector.ParseEngine(selector));

    [Fact]
    public void Engines_AreTheThreePlaywrightEngines()
        => Assert.Equal(new[] { "chromium", "firefox", "webkit" }, BrowserSelector.Engines);
}
