using System.Text.Json;
using AdbCore.Actions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Properties;

/// <summary>Editable value for one action config field. Normalizes the stored value (possibly a
/// <see cref="JsonElement"/> from a loaded bot) to the field's CLR type for display, and coerces
/// UI input back to that type on write.</summary>
public partial class ConfigFieldViewModel : ObservableObject
{
    private readonly NodeViewModel _node;
    private readonly Action _onChanged;

    public ConfigFieldViewModel(NodeViewModel node, ConfigField field, Action onChanged)
    {
        _node = node;
        Field = field;
        _onChanged = onChanged;
    }

    public ConfigField Field { get; }
    public string Key => Field.Key;
    public string Label => Field.Label;
    public ConfigFieldType Type => Field.Type;
    public IReadOnlyList<string> Options => Field.Options;

    public object? Value
    {
        get => Normalize(_node.Config.TryGetValue(Field.Key, out var v) ? v : Field.DefaultValue);
        set
        {
            _node.Config[Field.Key] = Coerce(value);
            OnPropertyChanged();
            _onChanged();
        }
    }

    private object? Normalize(object? raw)
    {
        if (Type == ConfigFieldType.Number)
        {
            return NormalizeNumber(raw);
        }

        if (raw is JsonElement json)
        {
            return Type switch
            {
                ConfigFieldType.Boolean => json.ValueKind is JsonValueKind.True or JsonValueKind.False && json.GetBoolean(),
                _ => json.ValueKind == JsonValueKind.String ? json.GetString() ?? string.Empty : json.ToString(),
            };
        }

        return Type switch
        {
            ConfigFieldType.Boolean => raw is bool b ? b : bool.TryParse(raw?.ToString(), out var bb) && bb,
            _ => raw?.ToString() ?? string.Empty,
        };
    }

    private object Coerce(object? input)
    {
        return Type switch
        {
            ConfigFieldType.Number => CoerceNumber(input),
            ConfigFieldType.Boolean => input is bool b ? b : bool.TryParse(input?.ToString(), out var bb) && bb,
            _ => input?.ToString() ?? string.Empty,
        };
    }

    // A Number field normally holds a double, but it also accepts a ${var} expression (the engine resolves
    // it to a number at run time). Such expressions are preserved as strings so they survive editing and
    // save/reload instead of being coerced to 0.
    private static object NormalizeNumber(object? raw)
    {
        if (raw is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Number)
            {
                return json.GetDouble();
            }

            var s = json.ValueKind == JsonValueKind.String ? json.GetString() ?? string.Empty : json.ToString();
            return IsExpression(s) ? s : double.TryParse(s, out var n) ? n : 0d;
        }

        if (raw is double d)
        {
            return d;
        }

        var text = raw?.ToString() ?? string.Empty;
        return IsExpression(text) ? text : double.TryParse(text, out var num) ? num : 0d;
    }

    private static object CoerceNumber(object? input)
    {
        if (input is double d)
        {
            return d;
        }

        var text = input?.ToString() ?? string.Empty;
        return IsExpression(text) ? text : double.TryParse(text, out var n) ? n : 0d;
    }

    private static bool IsExpression(string value) => value.Contains("${", StringComparison.Ordinal);
}
