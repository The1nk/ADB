using BotBuilder.Core.Targets;
using Xunit;

namespace BotBuilder.Core.Tests.Targets;

public class SelectorFormatTests
{
    [Fact]
    public void Window_WithProcess_UsesProcessSelector()
        => Assert.Equal("process:Notepad", SelectorFormat.Window("Notepad", "Untitled - Notepad"));

    [Fact]
    public void Window_EmptyProcess_FallsBackToTitle()
        => Assert.Equal("title:Some Window", SelectorFormat.Window("", "Some Window"));

    [Fact]
    public void Android_UsesSerial()
        => Assert.Equal("serial:emulator-5554", SelectorFormat.Android("emulator-5554"));

    [Fact]
    public void Browser_UsesEngine()
        => Assert.Equal("browser:chromium", SelectorFormat.Browser("chromium"));
}
