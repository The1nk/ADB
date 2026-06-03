using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class ExeLocatorTests
{
    [Fact]
    public void Locate_ReturnsFirstThatExists()
    {
        var found = ExeLocator.Locate(
            new[] { @"C:\a\BotRunner.exe", @"C:\b\BotRunner.exe" },
            exists: p => p == @"C:\b\BotRunner.exe");

        Assert.Equal(@"C:\b\BotRunner.exe", found);
    }

    [Fact]
    public void Locate_NoneExist_ReturnsNull()
    {
        Assert.Null(ExeLocator.Locate(new[] { @"C:\a\x.exe" }, exists: _ => false));
    }

    [Fact]
    public void Candidates_IncludesSiblingAndDevSibling()
    {
        var c = ExeLocator.Candidates(
            baseDir: @"C:\src\ADB\BotBuilder\bin\Debug\net10.0-windows",
            exeFileName: "BotRunner.exe");

        // Deployed: exe next to the Builder.
        Assert.Contains(@"C:\src\ADB\BotBuilder\bin\Debug\net10.0-windows\BotRunner.exe", c);
        // Dev: same bin/<cfg>/<tfm> layout under the sibling project.
        Assert.Contains(@"C:\src\ADB\BotRunner\bin\Debug\net10.0-windows\BotRunner.exe", c);
    }
}
