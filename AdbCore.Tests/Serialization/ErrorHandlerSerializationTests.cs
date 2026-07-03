using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using AdbCore.Serialization;
using Xunit;

namespace AdbCore.Tests.Serialization;

public class ErrorHandlerSerializationTests
{
    [Fact]
    public void Bot_WithErrorHandlerNode_RoundTrips()
    {
        var handlerId = Guid.NewGuid();
        var bot = new Bot
        {
            Id = Guid.NewGuid(),
            Name = "HasHandler",
            Actions =
            {
                new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" },
                new BotAction { Id = handlerId, TypeKey = ErrorHandlerAction.Key, Label = "Error Handler" },
            },
        };

        var serializer = new BotSerializer();
        var loaded = serializer.Deserialize(serializer.Serialize(bot));

        var handler = Assert.Single(loaded.Actions, a => a.TypeKey == ErrorHandlerAction.Key);
        Assert.Equal(handlerId, handler.Id);
    }
}
