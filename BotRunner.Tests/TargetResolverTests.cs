using AdbCore.Models;
using BotRunner;
using Xunit;

namespace BotRunner.Tests;

public class TargetResolverTests
{
    [Fact]
    public void Resolve_MapsSelectorsByTargetName()
    {
        var bot = new Bot();
        var id = Guid.NewGuid();
        bot.Targets.Add(new BotTarget { Id = id, Name = "Client 1", Type = BotTargetType.Window });
        var selectors = new Dictionary<string, string> { ["Client 1"] = "process:BlueStacks" };

        var resolved = TargetResolver.Resolve(bot, selectors);

        Assert.Equal("process:BlueStacks", resolved[id].Selector);
        Assert.Equal(BotTargetType.Window, resolved[id].Type);
    }

    [Fact]
    public void Resolve_DeclaredTargetWithoutSelector_Throws()
    {
        var bot = new Bot();
        bot.Targets.Add(new BotTarget { Id = Guid.NewGuid(), Name = "My Phone", Type = BotTargetType.AndroidDevice });

        var ex = Assert.Throws<CommandLineException>(
            () => TargetResolver.Resolve(bot, new Dictionary<string, string>()));
        Assert.Contains("My Phone", ex.Message);
    }

    [Fact]
    public void Resolve_NoTargets_ReturnsEmpty()
    {
        var resolved = TargetResolver.Resolve(new Bot(), new Dictionary<string, string>());

        Assert.Empty(resolved);
    }
}
