# MoonSharp Workflows Reference

## Contents
- Adding a New Lua Module
- Testing Modules with Fakes
- Debugging Script Failures
- Writing Bot Lua Scripts

---

## Adding a New Lua Module

Copy this checklist and track progress:

- [ ] Step 1: Define an interface for the external service (e.g., `IMyService`)
- [ ] Step 2: Create `AdbCore/Scripting/Modules/MyModule.cs` following the `FsModule`/`HttpModule` pattern
- [ ] Step 3: Add a constructor parameter + backing field to `LuaScriptHost`
- [ ] Step 4: Wire `script.Globals["mymodule"] = MyModule.Build(script, _myService, ct)` in `LuaScriptHost.Run()`
- [ ] Step 5: Add a live implementation of the interface
- [ ] Step 6: Update the production `LuaScriptHost(Action<string> log)` convenience constructor
- [ ] Step 7: Write xUnit tests using a fake service (see **xunit** skill)
- [ ] Step 8: Run `dotnet test ADB.slnx` — iterate until green

**Module skeleton:**

```csharp
// new code to add
internal static class MyModule
{
    public static Table Build(Script script, IMyService svc, CancellationToken ct)
    {
        var t = new Table(script);
        t["doThing"] = DynValue.NewCallback((ctx, args) =>
        {
            var input = args.AsType(0, "mymodule.doThing", DataType.String).String;
            MyResult result;
            try { result = svc.DoThing(input, ct); }
            catch (ScriptRuntimeException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { throw new ScriptRuntimeException(ex.Message); }

            var ret = new Table(script);
            ret["value"] = result.Value;
            return DynValue.NewTable(ret);
        });
        return t;
    }
}
```

---

## Testing Modules with Fakes

The project uses hand-rolled fakes (no mock frameworks). See the **xunit** skill for test conventions.

```csharp
// new code to add — AdbCore.Tests/Scripting/MyModuleTests.cs
public class MyModuleTests
{
    private sealed class FakeMyService : IMyService
    {
        public string? LastInput;
        public string ReturnValue = "default";
        public MyResult DoThing(string input, CancellationToken ct)
        {
            LastInput = input;
            return new MyResult(ReturnValue);
        }
    }

    [Fact]
    public void DoThing_ReturnsExpectedValue()
    {
        var fake = new FakeMyService { ReturnValue = "hello" };
        var host = new LuaScriptHost(_ => { }, new FakeFileSystem(), new FakeProcessRunner(), new FakeHttpRequester(), fake);
        var vars = new Dictionary<string, object>();
        var result = host.Run("vars['out'] = mymodule.doThing('input')['value']", vars, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("hello", vars["out"]);
    }
}
```

---

## Debugging Script Failures

`Result.Error` contains `InterpreterException.DecoratedMessage` — it includes the Lua source line and a partial stack trace. Log it in full.

1. Check `result.Error` — the line number points to the Lua source, not the C# host.
2. Wrap suspect calls in `pcall` to capture the error as a Lua value without aborting the script.
3. Use `log(tostring(vars["x"]))` to inspect variable state mid-script.
4. If `vars` round-trip produces wrong types, check `LuaValues.ToDynValue` — all numbers are `double`; integer arithmetic in Lua is fine, but CLR comparison with `int` may surprise.

**Validate with:**
```
dotnet test ADB.slnx --filter "FullyQualifiedName~Scripting"
```
Iterate until all scripting tests pass before testing in BotBuilder.

---

## Writing Bot Lua Scripts

Scripts run inside the `Run Lua Script` action node. Available globals:

| Global | Type | Purpose |
|--------|------|---------|
| `vars` | table | Bot variables (read/write; nil = unset) |
| `json` | table | `json.parse(str)` → table, `json.encode(table)` → string |
| `log` | function | `log(msg)` → appears in run log |
| `fs` | table | `read`, `write`, `copy`, `move`, `exists`, `delete` |
| `process` | table | `run(cmd, {args})` → `{exitCode, stdout, stderr}` |
| `http` | table | `get(url, headers?)`, `post(url, body?, headers?)` → `{status, body, headers}` |

**Script conventions:**

```lua
-- Read a variable (default to 0 if unset)
local count = vars["count"] or 0
vars["count"] = count + 1

-- Use pcall for risky I/O
local ok, err = pcall(function()
    local r = http.get("https://api.example.com/data")
    vars["data"] = json.parse(r.body)["value"]
end)
if not ok then log("Error: " .. tostring(err)) end

-- Unset a variable
vars["tempKey"] = nil
```

**DO:** Use `tostring()` before `log()` on non-string values — `log` expects a string or nil.

**DON'T:** Store tables in `vars` — the variable system is scalar (`string`/`double`/`bool`). Tables are stringified by `LuaValues.ToClr` and you'll lose structure silently.