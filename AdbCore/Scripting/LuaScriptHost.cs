using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Serialization.Json;

namespace AdbCore.Scripting;

/// <summary>Runs a user Lua script (MoonSharp) with a bidirectional <c>vars</c> bridge to the bot
/// variables, plus <c>json</c> and <c>log</c>. Pure core — no external I/O (http/fs/process are added by
/// M12b). A fresh <see cref="Script"/> is built per <see cref="Run"/> so scripts never share state.</summary>
public sealed class LuaScriptHost
{
    private readonly Action<string> _log;

    public LuaScriptHost(Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public readonly record struct Result(bool Success, string? Error);

    /// <summary>Runs <paramref name="scriptText"/>. On success, writes the final <c>vars</c> table back into
    /// <paramref name="variables"/>. A Lua syntax/runtime error (or <c>error(...)</c>) yields a failed Result.</summary>
    public Result Run(string scriptText, IDictionary<string, object> variables, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scriptText);
        ArgumentNullException.ThrowIfNull(variables);

        // A cancelled run is not a script "failure" — let OperationCanceledException propagate to the caller.
        // TODO(M12b): full mid-script abort once long-running http/process host calls (which honor ct) are
        // the realistic cancellation points. Until then, cancellation is checked once before execution.
        ct.ThrowIfCancellationRequested();

        // SoftSandbox: language features but no raw io/os/loadfile (the host API is provided explicitly).
        var script = new Script(CoreModules.Preset_SoftSandbox);

        // `vars` table seeded from the bot variables.
        var vars = new Table(script);
        foreach (var kv in variables)
        {
            vars[kv.Key] = LuaValues.ToDynValue(kv.Value);
        }
        script.Globals["vars"] = vars;

        // `json` table (MoonSharp's built-in JSON <-> Table converter).
        var json = new Table(script);
        json["parse"] = (Func<string, DynValue>)(s => DynValue.NewTable(JsonTableConverter.JsonToTable(s, script)));
        json["encode"] = (Func<DynValue, string>)(v =>
            v.Type == DataType.Table
                ? JsonTableConverter.TableToJson(v.Table)
                : throw new ScriptRuntimeException("json.encode expects a table"));
        script.Globals["json"] = json;

        // `log(msg)`.
        script.Globals["log"] = (Action<DynValue>)(v => _log(v is null || v.IsNil() ? "" : v.ToPrintString()));

        try
        {
            script.DoString(scriptText);
        }
        catch (InterpreterException ex)
        {
            return new Result(false, ex.DecoratedMessage ?? ex.Message);
        }

        // Write the final `vars` table back to the bot variables (string keys only).
        foreach (var pair in vars.Pairs)
        {
            if (pair.Key.Type == DataType.String)
            {
                variables[pair.Key.String] = LuaValues.ToClr(pair.Value)!;
            }
        }

        return new Result(true, null);
    }
}
