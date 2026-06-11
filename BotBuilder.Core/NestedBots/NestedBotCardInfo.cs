using AdbCore.Actions.BuiltIn;

namespace BotBuilder.Core.NestedBots;

/// <summary>Resolves the secondary line shown on a Nested Bot card: the referenced library bot's name, or a
/// placeholder when unassigned or dangling.</summary>
public static class NestedBotCardInfo
{
    public const string Unassigned = "(no bot assigned)";
    public const string Missing = "(missing bot)";

    public static string Resolve(IReadOnlyDictionary<string, object> config, NestedBotLibrary library)
    {
        if (!config.TryGetValue(NestedBotAction.NestedBotIdKey, out var raw)
            || !Guid.TryParse(raw?.ToString(), out var id))
        {
            return Unassigned;
        }
        return library.Get(id)?.Name ?? Missing;
    }
}
