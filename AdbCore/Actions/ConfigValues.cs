using System.Globalization;
using System.Text.Json;

namespace AdbCore.Actions;

/// <summary>Reads values out of an action's <c>Config</c> (or run variables), coercing the boxed
/// primitives stored in memory and the <see cref="JsonElement"/> values produced when a `.bot`
/// is loaded from disk. The <c>fallback</c> is returned only when the key is absent; a present
/// value is always coerced (an uncoercible bool/number reads as <c>false</c>/<c>0</c>, not the fallback).</summary>
public static class ConfigValues
{
    public static string GetString(IReadOnlyDictionary<string, object> config, string key, string fallback = "")
        => config.TryGetValue(key, out var raw) ? AsString(raw) : fallback;

    public static int GetInt(IReadOnlyDictionary<string, object> config, string key, int fallback = 0)
        => config.TryGetValue(key, out var raw) && TryAsDouble(raw, out var d) ? (int)d : fallback;

    public static double GetDouble(IReadOnlyDictionary<string, object> config, string key, double fallback = 0d)
        => config.TryGetValue(key, out var raw) && TryAsDouble(raw, out var d) ? d : fallback;

    public static bool GetBool(IReadOnlyDictionary<string, object> config, string key, bool fallback = false)
        => config.TryGetValue(key, out var raw) ? AsBool(raw) : fallback;

    /// <summary>Reads an int out of a run-variables dictionary, with the same coercion as config.</summary>
    public static int GetIntVar(IReadOnlyDictionary<string, object> variables, string key, int fallback = 0)
        => GetInt(variables, key, fallback);

    /// <summary>Coerces any config/variable value to its string form.</summary>
    public static string AsString(object? raw) => raw switch
    {
        null => string.Empty,
        string s => s,
        JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() ?? string.Empty : je.ToString(),
        _ => raw.ToString() ?? string.Empty,
    };

    /// <summary>Attempts to read a numeric value, handling boxed numbers, JSON numbers, and numeric strings.</summary>
    public static bool TryAsDouble(object? raw, out double value)
    {
        switch (raw)
        {
            case double d: value = d; return true;
            case int i: value = i; return true;
            case long l: value = l; return true;
            case float f: value = f; return true;
            case decimal m: value = (double)m; return true;
            case JsonElement je when je.ValueKind == JsonValueKind.Number: value = je.GetDouble(); return true;
            case JsonElement je when je.ValueKind == JsonValueKind.String:
                return double.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            case string s:
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            default: value = 0; return false;
        }
    }

    /// <summary>Coerces to bool: real bools, JSON true/false, "true"/"false" strings, or non-zero numbers.</summary>
    public static bool AsBool(object? raw)
    {
        switch (raw)
        {
            case bool b: return b;
            case JsonElement je when je.ValueKind == JsonValueKind.True: return true;
            case JsonElement je when je.ValueKind == JsonValueKind.False: return false;
        }

        if (bool.TryParse(AsString(raw), out var parsed))
        {
            return parsed;
        }

        return TryAsDouble(raw, out var number) && number != 0;
    }
}
