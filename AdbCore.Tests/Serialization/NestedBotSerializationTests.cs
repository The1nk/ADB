using AdbCore.Models;
using AdbCore.Serialization;
using Xunit;

namespace AdbCore.Tests.Serialization;

public class NestedBotSerializationTests
{
    [Fact]
    public void Bot_WithNestedLibrary_RoundTrips()
    {
        var nested = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "GoToPlayerMenu",
            Actions = { new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" } },
        };
        var parent = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            NestedBots = { nested },
            Actions =
            {
                new BotAction
                {
                    Id = Guid.NewGuid(),
                    TypeKey = "control.nestedBot",
                    Config = { ["nestedBotId"] = nested.Id.ToString() },
                },
            },
        };

        var serializer = new BotSerializer();
        var json = serializer.Serialize(parent);
        var loaded = serializer.Deserialize(json);

        Assert.Single(loaded.NestedBots);
        Assert.Equal("GoToPlayerMenu", loaded.NestedBots[0].Name);
        Assert.Equal(nested.Id, loaded.NestedBots[0].Id);
        Assert.Single(loaded.NestedBots[0].Actions);
    }

    [Fact]
    public void Bot_WithoutNestedBots_HasEmptyLibrary()
    {
        var loaded = new BotSerializer().Deserialize(new BotSerializer().Serialize(new Bot { Id = Guid.NewGuid(), Name = "Plain" }));
        Assert.Empty(loaded.NestedBots);
    }
}
