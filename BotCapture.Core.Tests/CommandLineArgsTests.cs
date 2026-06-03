using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class CommandLineArgsTests
{
    [Fact]
    public void NoArgs_IsStandalone()
    {
        var parsed = CommandLineArgs.Parse(Array.Empty<string>());
        Assert.Null(parsed.OutputPath);
        Assert.False(parsed.IsIntegrated);
    }

    [Fact]
    public void Output_SetsPathAndIntegrated()
    {
        var parsed = CommandLineArgs.Parse(new[] { "--output", @"C:\bots\attack.png" });
        Assert.Equal(@"C:\bots\attack.png", parsed.OutputPath);
        Assert.True(parsed.IsIntegrated);
    }

    [Fact]
    public void Output_MissingValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(new[] { "--output" }));
    }

    [Fact]
    public void UnknownArgument_Throws()
    {
        Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(new[] { "--bogus" }));
    }
}
