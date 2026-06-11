using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotCardInfoTests
{
    [Fact]
    public void Resolve_Unassigned_ReturnsPlaceholder()
    {
        var lib = new NestedBotLibrary();
        Assert.Equal(NestedBotCardInfo.Unassigned, NestedBotCardInfo.Resolve(new Dictionary<string, object>(), lib));
    }

    [Fact]
    public void Resolve_Assigned_ReturnsName()
    {
        var lib = new NestedBotLibrary();
        var bot = lib.AddNew("GoToPlayerMenu");
        var config = new Dictionary<string, object> { [NestedBotAction.NestedBotIdKey] = bot.Id.ToString() };
        Assert.Equal("GoToPlayerMenu", NestedBotCardInfo.Resolve(config, lib));
    }

    [Fact]
    public void Resolve_MissingReference_ReturnsMissing()
    {
        var lib = new NestedBotLibrary();
        var config = new Dictionary<string, object> { [NestedBotAction.NestedBotIdKey] = Guid.NewGuid().ToString() };
        Assert.Equal(NestedBotCardInfo.Missing, NestedBotCardInfo.Resolve(config, lib));
    }
}
