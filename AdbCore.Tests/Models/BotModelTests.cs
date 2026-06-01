using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Models;

public class BotModelTests
{
    [Fact]
    public void Bot_NewInstance_HasNonNullEmptyCollections()
    {
        var bot = new Bot();

        Assert.NotNull(bot.Targets);
        Assert.Empty(bot.Targets);
        Assert.NotNull(bot.Actions);
        Assert.Empty(bot.Actions);
        Assert.NotNull(bot.Connections);
        Assert.Empty(bot.Connections);
    }

    [Fact]
    public void BotAction_NewInstance_HasNonNullConfigAndPosition()
    {
        var action = new BotAction();

        Assert.NotNull(action.Config);
        Assert.Empty(action.Config);
        Assert.NotNull(action.CanvasPosition);
        Assert.Null(action.Retry);
        Assert.Null(action.TargetId);
    }

    [Fact]
    public void BotTarget_NewInstance_HasNonNullEmptyConfig()
    {
        var target = new BotTarget();

        Assert.NotNull(target.Config);
        Assert.Empty(target.Config);
    }

    [Fact]
    public void BotAction_PropertiesRoundTripThroughGetSet()
    {
        var id = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var action = new BotAction
        {
            Id = id,
            TypeKey = "screen.findImage",
            Label = "Find Attack Button",
            TargetId = targetId,
            Retry = new RetryPolicy { MaxAttempts = 5, DelayMs = 500 },
            CanvasPosition = new Position { X = 120, Y = 80 },
        };
        action.Config["confidence"] = 0.9;

        Assert.Equal(id, action.Id);
        Assert.Equal("screen.findImage", action.TypeKey);
        Assert.Equal("Find Attack Button", action.Label);
        Assert.Equal(targetId, action.TargetId);
        Assert.Equal(5, action.Retry!.MaxAttempts);
        Assert.Equal(500, action.Retry.DelayMs);
        Assert.Equal(120, action.CanvasPosition.X);
        Assert.Equal(80, action.CanvasPosition.Y);
        Assert.Equal(0.9, action.Config["confidence"]);
    }
}
