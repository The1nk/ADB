using AdbCore.Models;
using BotBuilder.Core.Targets;
using Xunit;

namespace BotBuilder.Core.Tests.Targets;

public class NodeTargetTypeTests
{
    [Theory]
    [InlineData("Android", BotTargetType.AndroidDevice)]
    [InlineData("Browser", BotTargetType.Browser)]
    [InlineData("Screen", BotTargetType.Window)]
    [InlineData("Input", BotTargetType.Window)]
    [InlineData("Window", BotTargetType.Window)]
    public void For_KnownCategories_MapToTargetType(string category, BotTargetType expected)
        => Assert.Equal(expected, NodeTargetType.For(category));

    [Theory]
    [InlineData("Control Flow")]
    [InlineData("Data")]
    [InlineData("Scripting")]
    [InlineData("Whatever")]
    public void For_TargetAgnosticCategories_ReturnNull(string category)
        => Assert.Null(NodeTargetType.For(category));
}
