using AdbCore.Targets;
using Xunit;

namespace AdbCore.Tests.Targets;

public class WindowSelectorTests
{
    [Fact]
    public void Parse_Process()
    {
        var s = WindowSelector.Parse("process:Notepad");
        Assert.Equal(WindowSelectorKind.Process, s.Kind);
        Assert.Equal("Notepad", s.Value);
    }

    [Fact]
    public void Parse_Title_KeepsValueVerbatimIncludingColons()
    {
        var s = WindowSelector.Parse("title:My App: Beta");
        Assert.Equal(WindowSelectorKind.Title, s.Kind);
        Assert.Equal("My App: Beta", s.Value); // only the first colon is the delimiter
    }

    [Fact]
    public void Parse_Hwnd()
    {
        var s = WindowSelector.Parse("hwnd:0x1A2B");
        Assert.Equal(WindowSelectorKind.Handle, s.Kind);
        Assert.Equal("0x1A2B", s.Value);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnPrefix()
        => Assert.Equal(WindowSelectorKind.Process, WindowSelector.Parse("PROCESS:x").Kind);

    [Fact]
    public void Parse_UnknownPrefix_Throws()
        => Assert.Throws<FormatException>(() => WindowSelector.Parse("serial:emulator-5554"));

    [Fact]
    public void Parse_NoColon_Throws()
        => Assert.Throws<FormatException>(() => WindowSelector.Parse("Notepad"));

    [Fact]
    public void Parse_EmptyValue_Throws()
        => Assert.Throws<FormatException>(() => WindowSelector.Parse("process:"));

    [Fact]
    public void Parse_LeadingColon_Throws()
        => Assert.Throws<FormatException>(() => WindowSelector.Parse(":Notepad"));

    [Fact]
    public void Parse_Null_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => WindowSelector.Parse(null!));
}
