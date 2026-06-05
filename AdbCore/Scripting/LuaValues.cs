using System.Text.Json;
using MoonSharp.Interpreter;

namespace AdbCore.Scripting;

/// <summary>Converts the bot variable system's CLR values (string/double/bool, plus JsonElement from a
/// loaded .bot) to MoonSharp <see cref="DynValue"/> and back to the canonical string/double/bool.</summary>
public static class LuaValues
{
    public static DynValue ToDynValue(object? value) => value switch
    {
        null => DynValue.Nil,
        string s => DynValue.NewString(s),
        bool b => DynValue.NewBoolean(b),
        double d => DynValue.NewNumber(d),
        float f => DynValue.NewNumber(f),
        int i => DynValue.NewNumber(i),
        long l => DynValue.NewNumber(l),
        JsonElement { ValueKind: JsonValueKind.Number } je => DynValue.NewNumber(je.GetDouble()),
        JsonElement { ValueKind: JsonValueKind.True } => DynValue.True,
        JsonElement { ValueKind: JsonValueKind.False } => DynValue.False,
        JsonElement je => DynValue.NewString(je.ValueKind == JsonValueKind.String ? je.GetString() ?? "" : je.ToString()),
        _ => DynValue.NewString(value.ToString() ?? string.Empty),
    };

    /// <summary>Maps a DynValue back to a canonical CLR value (string/double/bool), or null for nil.
    /// Tables/functions are flattened to their string form (the variable system holds scalars).</summary>
    public static object? ToClr(DynValue value) => value.Type switch
    {
        DataType.Nil or DataType.Void => null,
        DataType.String => value.String,
        DataType.Number => value.Number,
        DataType.Boolean => value.Boolean,
        _ => value.ToPrintString(),
    };
}
