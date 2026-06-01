using BotRunner;
using Xunit;

namespace BotRunner.Tests;

public class CommandLineArgsTests
{
    [Fact]
    public void Parse_BotPath_IsCaptured()
    {
        var args = CommandLineArgs.Parse(new[] { "--bot", @"C:\bots\farm.bot" });

        Assert.Equal(@"C:\bots\farm.bot", args.BotPath);
        Assert.Equal(LogLevel.Info, args.LogLevel);
        Assert.Empty(args.Targets);
    }

    [Fact]
    public void Parse_MissingBot_Throws()
    {
        Assert.Throws<CommandLineException>(() => CommandLineArgs.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void Parse_Targets_AreSplitOnFirstEquals()
    {
        var args = CommandLineArgs.Parse(new[]
        {
            "--bot", "b.bot",
            "--target", "Client 1=process:BlueStacks",
            "--target", "My Phone=serial:emulator-5554",
        });

        Assert.Equal("process:BlueStacks", args.Targets["Client 1"]);
        Assert.Equal("serial:emulator-5554", args.Targets["My Phone"]);
    }

    [Fact]
    public void Parse_TargetWithoutEquals_Throws()
    {
        Assert.Throws<CommandLineException>(
            () => CommandLineArgs.Parse(new[] { "--bot", "b.bot", "--target", "bogus" }));
    }

    [Fact]
    public void Parse_LogLevel_IsCaseInsensitive()
    {
        var args = CommandLineArgs.Parse(new[] { "--bot", "b.bot", "--log-level", "DEBUG" });

        Assert.Equal(LogLevel.Debug, args.LogLevel);
    }

    [Fact]
    public void Parse_UnknownLogLevel_Throws()
    {
        Assert.Throws<CommandLineException>(
            () => CommandLineArgs.Parse(new[] { "--bot", "b.bot", "--log-level", "loud" }));
    }

    [Fact]
    public void Parse_UnknownArgument_Throws()
    {
        Assert.Throws<CommandLineException>(
            () => CommandLineArgs.Parse(new[] { "--bot", "b.bot", "--wat" }));
    }

    [Fact]
    public void Parse_FlagWithoutValue_Throws()
    {
        Assert.Throws<CommandLineException>(() => CommandLineArgs.Parse(new[] { "--bot" }));
    }
}
