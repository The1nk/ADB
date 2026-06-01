namespace AdbCore.Models;

/// <summary>A named automation target — window, Android device, or browser context.</summary>
public class BotTarget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public BotTargetType Type { get; set; }

    /// <summary>Type-specific configuration, e.g. { "selector": "process:BlueStacks" }.</summary>
    public Dictionary<string, string> Config { get; set; } = new();
}
