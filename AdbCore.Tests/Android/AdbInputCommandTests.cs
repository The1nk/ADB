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

    [Fact]
    public void AdbKeyboardText_BroadcastsBase64Utf8()
    {
        const string prefix = "am broadcast -a ADB_INPUT_B64 --es msg '";
        var cmd = AdbInputCommand.AdbKeyboardText("¹²³");
        Assert.StartsWith(prefix, cmd);
        Assert.EndsWith("'", cmd);
        var b64 = cmd[prefix.Length..^1];
        var decoded = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(b64));
        Assert.Equal("¹²³", decoded);
    }

    [Fact]
    public void AdbKeyboardText_Empty_EncodesEmptyPayload()
        => Assert.Equal("am broadcast -a ADB_INPUT_B64 --es msg ''", AdbInputCommand.AdbKeyboardText(""));

    [Fact]
    public void ImeCommands_AreExact()
    {
        Assert.Equal("ime set com.android.adbkeyboard/.AdbIME", AdbInputCommand.SetIme("com.android.adbkeyboard/.AdbIME"));
        Assert.Equal("ime enable com.android.adbkeyboard/.AdbIME", AdbInputCommand.EnableIme("com.android.adbkeyboard/.AdbIME"));
        Assert.Equal("settings get secure default_input_method", AdbInputCommand.GetDefaultIme());
        Assert.Equal("ime list -a -s", AdbInputCommand.ListImes());
    }
}
