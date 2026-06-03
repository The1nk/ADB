using System.Text.RegularExpressions;
using AdbCore.Actions;
using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Resolves <c>${variableName}</c> tokens in an action's string config values against the run
/// variables, immediately before the action executes. Unknown variables resolve to empty string;
/// non-string config values pass through untouched; the original action/config is never mutated.</summary>
public static partial class ConfigInterpolator
{
    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex TokenRegex();

    /// <summary>Replaces each <c>${name}</c> in <paramref name="template"/> with the string form of
    /// <c>variables[name]</c> (name trimmed; unknown ⇒ empty). A value with no <c>${</c> is returned as-is.</summary>
    public static string Interpolate(string template, IReadOnlyDictionary<string, object> variables)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("${", StringComparison.Ordinal))
        {
            return template;
        }

        return TokenRegex().Replace(template, m =>
        {
            var name = m.Groups[1].Value.Trim();
            return variables.TryGetValue(name, out var value) ? ConfigValues.AsString(value) : string.Empty;
        });
    }

    /// <summary>Returns <paramref name="action"/> unchanged when no string config value contains a token;
    /// otherwise a clone whose Config has string values interpolated (non-strings pass through).</summary>
    public static BotAction Resolve(BotAction action, IReadOnlyDictionary<string, object> variables)
    {
        var hasToken = false;
        foreach (var v in action.Config.Values)
        {
            if (v is string s && s.Contains("${", StringComparison.Ordinal))
            {
                hasToken = true;
                break;
            }
        }

        if (!hasToken)
        {
            return action;
        }

        var resolved = new Dictionary<string, object>(action.Config.Count);
        foreach (var (key, value) in action.Config)
        {
            resolved[key] = value is string s ? Interpolate(s, variables) : value;
        }

        return action.CloneWithConfig(resolved);
    }
}
