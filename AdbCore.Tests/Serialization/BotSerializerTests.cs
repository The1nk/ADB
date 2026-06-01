using System.Text.Json.Nodes;
using AdbCore.Models;
using AdbCore.Serialization;
using Xunit;

namespace AdbCore.Tests.Serialization;

public class BotSerializerTests
{
    private static Bot BuildSampleBot()
    {
        var targetWindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var targetPhoneId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var actionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var action2Id = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var bot = new Bot
        {
            Id = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1"),
            Name = "Farm Gold",
            Description = "Sample bot",
            CreatedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 6, 1, 12, 30, 0, DateTimeKind.Utc),
        };
        bot.Targets.Add(new BotTarget
        {
            Id = targetWindowId,
            Name = "Client 1",
            Type = BotTargetType.Window,
            Config = { ["selector"] = "process:BlueStacks" },
        });
        bot.Targets.Add(new BotTarget
        {
            Id = targetPhoneId,
            Name = "My Phone",
            Type = BotTargetType.AndroidDevice,
            Config = { ["selector"] = "serial:emulator-5554" },
        });

        var findImage = new BotAction
        {
            Id = actionId,
            TypeKey = "screen.findImage",
            Label = "Find Attack Button",
            TargetId = targetWindowId,
            Retry = new RetryPolicy { MaxAttempts = 5, DelayMs = 500 },
            CanvasPosition = new Position { X = 120, Y = 80 },
        };
        findImage.Config["templatePath"] = "assets/attack-btn.png";
        findImage.Config["confidence"] = 0.9;
        bot.Actions.Add(findImage);

        bot.Connections.Add(new ActionConnection
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            SourceActionId = actionId,
            SourcePort = "onSuccess",
            TargetActionId = action2Id,
            TargetPort = "input",
        });

        return bot;
    }

    [Fact]
    public void Serialize_Deserialize_Serialize_IsStable()
    {
        var serializer = new BotSerializer();
        var bot = BuildSampleBot();

        var json1 = serializer.Serialize(bot);
        var bot2 = serializer.Deserialize(json1);
        var json2 = serializer.Serialize(bot2);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Serialize_IncludesVersionEnvelope()
    {
        var serializer = new BotSerializer();

        var json = serializer.Serialize(BuildSampleBot());
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("1.0", root["version"]!.GetValue<string>());
    }

    [Fact]
    public void Serialize_WritesEnumsAsStrings()
    {
        var serializer = new BotSerializer();

        var json = serializer.Serialize(BuildSampleBot());
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("Window", root["targets"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("AndroidDevice", root["targets"]![1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Serialize_WritesCanvasPositionAsPosition()
    {
        var serializer = new BotSerializer();

        var json = serializer.Serialize(BuildSampleBot());
        var root = JsonNode.Parse(json)!.AsObject();
        var action = root["actions"]![0]!.AsObject();

        Assert.NotNull(action["position"]);
        Assert.False(action.ContainsKey("canvasPosition"));
        Assert.Equal(120, action["position"]!["x"]!.GetValue<double>());
    }

    [Fact]
    public void Deserialize_PreservesStronglyTypedFields()
    {
        var serializer = new BotSerializer();
        var bot = BuildSampleBot();

        var roundTripped = serializer.Deserialize(serializer.Serialize(bot));

        Assert.Equal(bot.Name, roundTripped.Name);
        Assert.Equal(bot.Id, roundTripped.Id);
        Assert.Equal(2, roundTripped.Targets.Count);
        Assert.Equal(BotTargetType.AndroidDevice, roundTripped.Targets[1].Type);
        Assert.Equal(5, roundTripped.Actions[0].Retry!.MaxAttempts);
        Assert.Equal("onSuccess", roundTripped.Connections[0].SourcePort);
    }

    [Fact]
    public void SaveLoad_RoundTripsThroughDisk()
    {
        var serializer = new BotSerializer();
        var bot = BuildSampleBot();
        var path = Path.Combine(Path.GetTempPath(), $"adb-test-{Guid.NewGuid():N}.bot");

        try
        {
            serializer.Save(bot, path);
            var loaded = serializer.Load(path);

            Assert.Equal(serializer.Serialize(bot), serializer.Serialize(loaded));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Deserialize_UnsupportedVersion_Throws()
    {
        var serializer = new BotSerializer();

        Assert.Throws<NotSupportedException>(
            () => serializer.Deserialize("{\"version\":\"0.1\"}"));
    }
}
