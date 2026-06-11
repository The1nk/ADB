namespace AdbCore.Models;

/// <summary>A named automation target — window, Android device, or browser context.</summary>
public class BotTarget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public BotTargetType Type { get; set; }

    /// <summary>Type-specific configuration, e.g. { "selector": "process:BlueStacks" }.</summary>
    public Dictionary<string, string> Config { get; set; } = new();

    /// <summary>Convenience accessor for <c>Config["selector"]</c> — the target's type-specific selector string
    /// (e.g. "process:BlueStacks", "serial:emulator-5554", "browser:chromium"). Returns an empty string when
    /// the key is absent (no selector configured).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Selector
    {
        get => Config.TryGetValue("selector", out var s) ? s : string.Empty;
        set => Config["selector"] = value;
    }
}
