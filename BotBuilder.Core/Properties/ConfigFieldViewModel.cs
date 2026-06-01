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
        if (raw is JsonElement json)
        {
            return Type switch
            {
                ConfigFieldType.Number => json.ValueKind == JsonValueKind.Number ? json.GetDouble() : 0d,
                ConfigFieldType.Boolean => json.ValueKind is JsonValueKind.True or JsonValueKind.False && json.GetBoolean(),
                _ => json.ValueKind == JsonValueKind.String ? json.GetString() ?? string.Empty : json.ToString(),
            };
        }

        return Type switch
        {
            ConfigFieldType.Number => raw is double d ? d : double.TryParse(raw?.ToString(), out var n) ? n : 0d,
            ConfigFieldType.Boolean => raw is bool b ? b : bool.TryParse(raw?.ToString(), out var bb) && bb,
            _ => raw?.ToString() ?? string.Empty,
        };
    }

    private object Coerce(object? input)
    {
        return Type switch
        {
            ConfigFieldType.Number => input is double d ? d : double.TryParse(input?.ToString(), out var n) ? n : 0d,
            ConfigFieldType.Boolean => input is bool b ? b : bool.TryParse(input?.ToString(), out var bb) && bb,
            _ => input?.ToString() ?? string.Empty,
        };
    }
}
