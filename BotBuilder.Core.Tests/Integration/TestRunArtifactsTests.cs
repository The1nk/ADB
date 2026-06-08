using System.IO;
using BotBuilder.Core.Integration;
using Xunit;

namespace BotBuilder.Core.Tests.Integration;

public class TestRunArtifactsTests
{
    [Fact]
    public void SafeFileName_StripsInvalidCharacters()
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = "My" + invalid[0] + "Bot" + invalid[^1];

        Assert.Equal("MyBot", TestRunArtifacts.SafeFileName(name));
    }

    [Fact]
    public void SafeFileName_KeepsValidName()
    {
        Assert.Equal("Grinder 9000", TestRunArtifacts.SafeFileName("Grinder 9000"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SafeFileName_EmptyOrWhitespace_FallsBackToBot(string name)
    {
        Assert.Equal("bot", TestRunArtifacts.SafeFileName(name));
    }

    [Fact]
    public void SafeFileName_AllInvalidChars_FallsBackToBot()
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(new[] { invalid[0], invalid[1] });

        Assert.Equal("bot", TestRunArtifacts.SafeFileName(name));
    }

    [Fact]
    public void TempBotPath_ComposesRootSubdirAndSafeName()
    {
        var path = TestRunArtifacts.TempBotPath("C:\\temp", "My Bot");

        Assert.Equal(Path.Combine("C:\\temp", "adb-testrun", "My Bot.bot"), path);
    }
}
