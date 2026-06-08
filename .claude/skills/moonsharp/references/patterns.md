# MoonSharp Patterns Reference

## Contents
- Module Table Pattern
- CLR Type Bridging
- Error Handling
- Cancellation
- Anti-Patterns

---

## Module Table Pattern

All built-in Lua modules follow the same structure: a static `Build` method returns a `Table` registered as a `script.Globals` entry. I/O failures wrap in `ScriptRuntimeException` so Lua can `pcall` them.

```csharp
// AdbCore/Scripting/Modules/FsModule.cs — existing
internal static class FsModule
{
    public static Table Build(Script script, IFileSystem fs)
    {
        var t = new Table(script);
        t["read"]   = (Func<string, string>)(p => Guard(() => fs.Read(p)));
        t["exists"] = (Func<string, bool>)(p => Guard(() => fs.Exists(p)));
        return t;
    }

    private static T Guard<T>(Func<T> op)
    {
        try { return op(); }
        catch (ScriptRuntimeException) { throw; }
        catch (Exception ex) { throw new ScriptRuntimeException(ex.Message); }
    }
}
```

**DO:** Always re-throw `ScriptRuntimeException` and `OperationCanceledException` before the generic catch. Swallowing them loses Lua stack decoration and breaks cancellation.

**DON'T:** Throw raw CLR exceptions from a callback — MoonSharp won't decorate them with Lua line info and the user sees a confusing internal error.

---

## CLR Type Bridging

`LuaValues` is the single conversion point. Bot variables are `string | double | bool | null`. Everything else (int, float, long, `JsonElement`) is normalized on the way in; tables and functions are stringified on the way out.

```csharp
// AdbCore/Scripting/LuaValues.cs — existing
public static DynValue ToDynValue(object? value) => value switch
{
    null   => DynValue.Nil,
    string s => DynValue.NewString(s),
    bool b   => DynValue.NewBoolean(b),
    double d => DynValue.NewNumber(d),
    int i    => DynValue.NewNumber(i),
    // JsonElement variants handled too
    _        => DynValue.NewString(value.ToString() ?? string.Empty),
};
```

**DO:** Use `LuaValues.ToDynValue` / `LuaValues.ToClr` for every CLR↔Lua conversion. This keeps the type contract consistent across all scripts.

**DON'T:** Set `vars[key]` directly with arbitrary CLR objects. MoonSharp may accept them as `UserData` without `UserData.RegisterType`, causing `ScriptRuntimeException` at runtime.

---

## Error Handling

Lua errors (syntax, runtime, `error(...)`) all become `InterpreterException`. Use `DecoratedMessage` for the full Lua stack trace.

```csharp
// AdbCore/Scripting/LuaScriptHost.cs — existing
catch (InterpreterException ex)
{
    return new Result(false, ex.DecoratedMessage ?? ex.Message);
}
```

From Lua, wrap risky host calls with `pcall`:

```lua
local ok, err = pcall(function()
    local r = http.get("https://flaky-api.example.com/data")
    vars["result"] = r.body
end)
if not ok then
    log("HTTP failed: " .. err)
    vars["failed"] = true
end
```

---

## Cancellation

`CancellationToken` is threaded through to all blocking host calls (http, process). CPU-bound Lua loops are interrupted via `AutoYieldCounter`:

```csharp
// AdbCore/Scripting/LuaScriptHost.cs — existing
var coroutine = script.CreateCoroutine(fn).Coroutine;
coroutine.AutoYieldCounter = 20000;
DynValue exec = coroutine.Resume();
while (exec.Type == DataType.YieldRequest)
{
    ct.ThrowIfCancellationRequested();
    exec = coroutine.Resume();
}
```

**DO:** Pass `ct` into every module `Build` call that does I/O. The host checks it between coroutine resumes; the modules check it in blocking calls.

**DON'T:** Use `Task.Run` inside a module callback to work around blocking — this loses the cancellation chain and leaks threads.

---

## Anti-Patterns

### WARNING: Using `Preset_Complete`

**The Problem:**
```csharp
// BAD — exposes raw OS/IO to user scripts
var script = new Script(CoreModules.Preset_Complete);
```

**Why This Breaks:**
1. Exposes `io.open`, `os.execute`, `loadfile` — user scripts can read arbitrary files or run shell commands.
2. Breaks the security model: bot scripts are user-supplied and untrusted.

**The Fix:**
```csharp
// GOOD — existing pattern in LuaScriptHost
var script = new Script(CoreModules.Preset_SoftSandbox);
```

---

### WARNING: Unregistered UserData

**The Problem:**
```csharp
// BAD — CLR object without UserData.RegisterType
script.Globals["myObj"] = new MyComplexClass();
```

**Why This Breaks:**
1. MoonSharp can't proxy unregistered types; throws `ScriptRuntimeException` when Lua accesses any member.
2. The error appears at Lua runtime, not at C# setup time — hard to diagnose.

**The Fix:** Either register the type (`UserData.RegisterType<MyComplexClass>()`) before use, or expose only primitive values and Table-based APIs as all existing modules do.