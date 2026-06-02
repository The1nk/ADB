using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>A bot target resolved at run start. The selector is always captured; the live handle is
/// populated by a type-specific binder (e.g. WindowTargetBinder resolves a Window selector to an HWND).</summary>
public class ResolvedTarget
{
    public BotTargetType Type { get; set; }

    /// <summary>The raw selector provided at runtime, e.g. "process:BlueStacks".</summary>
    public string Selector { get; set; } = string.Empty;

    /// <summary>The live handle (e.g. an IntPtr HWND for Window targets), populated at run start by the
    /// relevant binder. Null for target types whose binder is not yet implemented (Android / Browser).</summary>
    public object? Handle { get; set; }
}
