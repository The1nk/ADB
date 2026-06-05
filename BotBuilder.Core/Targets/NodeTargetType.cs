using AdbCore.Models;

namespace BotBuilder.Core.Targets;

/// <summary>Maps an action's design-time <c>Category</c> to the target type its nodes act on, used to
/// auto-assign a target when a node is added. Window-acting categories (Screen, Input — both resolve to a
/// window HWND at runtime) map to <see cref="BotTargetType.Window"/>; target-agnostic categories
/// (Control Flow / Data / Scripting / unknown) map to null.</summary>
public static class NodeTargetType
{
    public static BotTargetType? For(string category) => category switch
    {
        "Android" => BotTargetType.AndroidDevice,
        "Browser" => BotTargetType.Browser,
        "Screen" => BotTargetType.Window,
        "Input" => BotTargetType.Window,
        _ => null,
    };
}
