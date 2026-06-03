using System.Text.Json;
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

    /// <summary>Returns <paramref name="action"/> unchanged when no config value carries a token;
    /// otherwise a clone whose textual config values have their tokens interpolated (non-text values
    /// pass through). Handles both in-memory strings and the <see cref="JsonElement"/> string values a
    /// bot deserializes to when loaded from disk.</summary>
    public static BotAction Resolve(BotAction action, IReadOnlyDictionary<string, object> variables)
    {
        var hasToken = false;
        foreach (var v in action.Config.Values)
        {
            if (ContainsToken(v, out _))
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
            resolved[key] = ContainsToken(value, out var text) ? Interpolate(text, variables) : value;
        }

        return action.CloneWithConfig(resolved);
    }

    // A config value carries an expression when it is textual — a CLR string, or a JSON string from a
    // loaded .bot — AND contains a ${ token. A plain `is string` check would miss loaded bots (whose
    // config values are JsonElement), so coerce JSON string values here too. Non-text values are ignored.
    private static bool ContainsToken(object? value, out string text)
    {
        text = string.Empty;
        var s = value switch
        {
            string str => str,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => null,
        };

        if (s is not null && s.Contains("${", StringComparison.Ordinal))
        {
            text = s;
            return true;
        }

        return false;
    }
}
