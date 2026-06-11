using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Models;

public class BotToStringTests
{
    [Fact]
    public void ToString_ReturnsName()
    {
        var bot = new Bot { Id = Guid.NewGuid(), Name = "GoToPlayerMenu" };
        Assert.Equal("GoToPlayerMenu", bot.ToString());
    }

    [Fact]
    public void ToString_BlankName_ReturnsPlaceholder()
    {
        Assert.Equal("(unnamed bot)", new Bot { Id = Guid.NewGuid(), Name = "  " }.ToString());
    }
}
