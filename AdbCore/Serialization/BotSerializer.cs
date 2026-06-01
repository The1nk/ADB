using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AdbCore.Models;

namespace AdbCore.Serialization;

/// <summary>Reads and writes <see cref="Bot"/> instances as version-tagged `.bot` JSON files.</summary>
public class BotSerializer
{
    /// <summary>The schema version this serializer reads and writes.</summary>
    public const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes a bot to its `.bot` JSON representation, with the version envelope first.</summary>
    public string Serialize(Bot bot)
    {
        var body = JsonSerializer.SerializeToNode(bot, Options)!.AsObject();

        // Rebuild so "version" is the first property; re-parenting requires detaching from `body`.
        var result = new JsonObject { ["version"] = SchemaVersion };
        foreach (var property in body.ToArray())
        {
            body.Remove(property.Key);
            result[property.Key] = property.Value;
        }

        return result.ToJsonString(Options);
    }

    /// <summary>Parses `.bot` JSON into a <see cref="Bot"/>, validating the schema version.</summary>
    public Bot Deserialize(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException("Bot content is not a JSON object.");

        var version = root["version"]?.GetValue<string>();
        if (version != SchemaVersion)
        {
            throw new NotSupportedException(
                $"Unsupported .bot version '{version ?? "(none)"}'. Expected '{SchemaVersion}'.");
        }

        return root.Deserialize<Bot>(Options)
            ?? throw new InvalidDataException("Failed to deserialize bot.");
    }

    /// <summary>Serializes a bot and writes it to <paramref name="path"/>.</summary>
    public void Save(Bot bot, string path)
        => File.WriteAllText(path, Serialize(bot));

    /// <summary>Reads and deserializes a bot from <paramref name="path"/>.</summary>
    public Bot Load(string path)
        => Deserialize(File.ReadAllText(path));
}
