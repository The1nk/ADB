using AdbCore.Android;
using Xunit;

namespace AdbCore.Tests.Android;

public class AdbInputCommandTests
{
    [Fact]
    public void LongPress_IsSamePointSwipeWithDuration()
        => Assert.Equal("input swipe 5 10 5 10 600", AdbInputCommand.LongPress(5, 10, 600));

    [Fact]
    public void Text_SingleQuoteWrapsPlainText()
        => Assert.Equal("input text 'hello world'", AdbInputCommand.Text("hello world"));

    [Fact]
    public void Text_EscapesEmbeddedSingleQuote()
        => Assert.Equal(@"input text 'it'\''s me'", AdbInputCommand.Text("it's me"));

    [Fact]
    public void Text_EmptyStillProducesQuotedEmptyArg()
        => Assert.Equal("input text ''", AdbInputCommand.Text(""));

    [Fact]
    public void KeyEvent_RepeatsCodeCountTimes()
        => Assert.Equal("input keyevent 67 67 67", AdbInputCommand.KeyEvent(67, 3));

    [Fact]
    public void KeyEvent_CountOne_IsSingleCode()
        => Assert.Equal("input keyevent 66", AdbInputCommand.KeyEvent(66, 1));

    [Fact]
    public void KeyEvent_CountBelowOne_ClampsToOne()
        => Assert.Equal("input keyevent 67", AdbInputCommand.KeyEvent(67, 0));
}
