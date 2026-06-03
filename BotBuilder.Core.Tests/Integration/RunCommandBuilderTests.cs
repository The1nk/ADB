using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class RunCommandBuilderTests
{
    [Fact]
    public void BuildArgs_BotAndTargets_ProducesFlagList()
    {
        var args = RunCommandBuilder.BuildArgs(
            @"C:\bots\farm.bot",
            new[] { ("Client 1", "process:BlueStacks"), ("My Phone", "serial:emulator-5554") });

        Assert.Equal(
            new[] { "--bot", @"C:\bots\farm.bot",
                    "--target", "Client 1=process:BlueStacks",
                    "--target", "My Phone=serial:emulator-5554" },
            args);
    }

    [Fact]
    public void BuildDisplayCommand_QuotesValuesWithSpaces()
    {
        var cmd = RunCommandBuilder.BuildDisplayCommand(
            "BotRunner.exe",
            @"C:\my bots\farm.bot",
            new[] { ("Client 1", "process:BlueStacks") });

        Assert.Equal(
            "BotRunner.exe --bot \"C:\\my bots\\farm.bot\" --target \"Client 1=process:BlueStacks\"",
            cmd);
    }

    [Fact]
    public void BuildDisplayCommand_NoTargets_JustBot()
    {
        var cmd = RunCommandBuilder.BuildDisplayCommand("BotRunner.exe", @"C:\farm.bot", Array.Empty<(string, string)>());
        Assert.Equal("BotRunner.exe --bot C:\\farm.bot", cmd);
    }
}
