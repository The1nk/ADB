using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Serialization.Json;

namespace AdbCore.Scripting;

/// <summary>Runs a user Lua script (MoonSharp) with a bidirectional <c>vars</c> bridge to the bot
/// variables, plus <c>json</c> and <c>log</c>. Pure core — no external I/O (http/fs/process are added by
/// M12b). A fresh <see cref="Script"/> is built per <see cref="Run"/> so scripts never share state.</summary>
public sealed class LuaScriptHost
{
    private readonly Action<string> _log;
    private readonly IFileSystem _fs;
    private readonly IProcessRunner _process;
    private readonly IHttpRequester _http;

    public LuaScriptHost(Action<string> log)
        : this(log, new LiveFileSystem(), new LiveProcessRunner(), new HttpRequester()) { }

    public LuaScriptHost(Action<string> log, IFileSystem fileSystem, IProcessRunner processRunner, IHttpRequester httpRequester)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(httpRequester);
        _log = log;
        _fs = fileSystem;
        _process = processRunner;
        _http = httpRequester;
    }

    public readonly record struct Result(bool Success, string? Error);

    /// <summary>Runs <paramref name="scriptText"/>. On success, writes the final <c>vars</c> table back into
    /// <paramref name="variables"/>. A Lua syntax/runtime error (or <c>error(...)</c>) yields a failed Result.</summary>
    public Result Run(string scriptText, IDictionary<string, object> variables, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scriptText);
        ArgumentNullException.ThrowIfNull(variables);

        // A cancelled run is not a script "failure" — let OperationCanceledException propagate to the caller.
        // Mid-script CPU-bound work (e.g. `while true do end`) is interrupted cooperatively below via a
        // coroutine that auto-yields every N VM instructions; blocking I/O host calls honor ct directly.
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

        // `fs` table (read/write/copy/move/exists/delete).
        script.Globals["fs"] = Modules.FsModule.Build(script, _fs);

        // `process` table (run -> { exitCode, stdout, stderr }).
        script.Globals["process"] = Modules.ProcessModule.Build(script, _process, ct);

        // `http` table (get/post -> { status, body, headers }).
        script.Globals["http"] = Modules.HttpModule.Build(script, _http, ct);

        try
        {
            var fn = script.LoadString(scriptText);
            var coroutine = script.CreateCoroutine(fn).Coroutine;
            coroutine.AutoYieldCounter = 20000; // auto-yield every N VM instructions
            DynValue exec = coroutine.Resume();
            while (exec.Type == DataType.YieldRequest)
            {
                ct.ThrowIfCancellationRequested();
                exec = coroutine.Resume();
            }
        }
        catch (InterpreterException ex)
        {
            return new Result(false, ex.DecoratedMessage ?? ex.Message);
        }

        // Write the final `vars` table back to the bot variables (string keys only). A nil final value
        // means "unset" (spec model: nil = unset), so remove the key rather than storing a CLR null.
        // Pre-existing keys that the script set to nil are dropped from the Lua table and so never appear
        // in vars.Pairs — handle their removal explicitly by checking the current table value per key.
        foreach (var key in variables.Keys.ToList())
        {
            if (vars.Get(key).IsNil())
                variables.Remove(key);
        }
        foreach (var pair in vars.Pairs)
        {
            if (pair.Key.Type != DataType.String) continue;
            var key = pair.Key.String;
            if (pair.Value.IsNil())
                variables.Remove(key);
            else
                variables[key] = LuaValues.ToClr(pair.Value)!;
        }

        return new Result(true, null);
    }
}
