using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>A bot target resolved at run start. In M2 only the selector is captured;
/// opening a live handle (HWND / ADB device / Playwright page) is deferred to M7.</summary>
public class ResolvedTarget
{
    public BotTargetType Type { get; set; }

    /// <summary>The raw selector provided at runtime, e.g. "process:BlueStacks".</summary>
    public string Selector { get; set; } = string.Empty;

    /// <summary>The live handle, populated in later milestones. Null in M2.</summary>
    public object? Handle { get; set; }
}
