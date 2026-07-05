using System.Text.Json.Serialization;

namespace AdbCore.Models;

/// <summary>A single action node in the bot graph.</summary>
public class BotAction
{
    public Guid Id { get; set; }

    /// <summary>The action type key, e.g. "screen.findImage", "android.tap".</summary>
    public string TypeKey { get; set; } = string.Empty;

    /// <summary>User-editable display name.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Which target this action operates on; null means the first target.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>Action-specific settings.</summary>
    public Dictionary<string, object> Config { get; set; } = new();

    /// <summary>Optional per-action retry configuration.</summary>
    public RetryPolicy? Retry { get; set; }

    /// <summary>True when the editor flipped this node's ports for a right-to-left serpentine band
    /// (inputs on the right, outputs on the left). Optional; defaults false for older files.</summary>
    public bool PortsFlipped { get; set; }

    [JsonPropertyName("position")]
    public Position CanvasPosition { get; set; } = new();

    /// <summary>Returns a shallow copy of this action with <paramref name="config"/> in place of
    /// <see cref="Config"/> — used to execute an action against an interpolated config without
    /// mutating the stored node.</summary>
    public BotAction CloneWithConfig(Dictionary<string, object> config) => new()
    {
        Id = Id,
        TypeKey = TypeKey,
        Label = Label,
        TargetId = TargetId,
        Config = config,
        Retry = Retry,
        CanvasPosition = CanvasPosition,
        PortsFlipped = PortsFlipped,
    };
}
