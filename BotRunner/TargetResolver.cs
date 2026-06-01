using AdbCore.Execution;
using AdbCore.Models;

namespace BotRunner;

/// <summary>Resolves the targets declared in a bot against the selectors supplied on the CLI.</summary>
public static class TargetResolver
{
    public static Dictionary<Guid, ResolvedTarget> Resolve(Bot bot, IReadOnlyDictionary<string, string> selectors)
    {
        var resolved = new Dictionary<Guid, ResolvedTarget>();

        foreach (var target in bot.Targets)
        {
            if (!selectors.TryGetValue(target.Name, out var selector))
            {
                throw new CommandLineException(
                    $"Target '{target.Name}' declared in the bot has no matching --target argument.");
            }

            resolved[target.Id] = new ResolvedTarget { Type = target.Type, Selector = selector };
        }

        return resolved;
    }
}
