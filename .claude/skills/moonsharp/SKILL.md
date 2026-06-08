---
name: moonsharp
description: |
  Lua scripting engine (MoonSharp 2.0.x) embedded in AdbCore for bot extensibility. Provides a sandboxed Lua environment with bidirectional `vars` bridge and built-in modules: http, json, fs, process, log.
  Use when: adding or modifying Lua scripting support, extending built-in Lua modules, writing bot Lua scripts, or debugging script execution failures.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# MoonSharp Skill

MoonSharp is the Lua scripting engine embedded in `AdbCore/Scripting/`. Each bot `Run Lua Script` action spawns a fresh `Script` instance in `CoreModules.Preset_SoftSandbox` — no shared state between runs. The `vars` table is the bidirectional bridge between Lua and bot variables; all other bot-level I/O goes through the five built-in module tables.

## Before You Code (REQUIRED)

This skill's content was captured at generation time and MAY be stale. For ANY non-trivial change involving moonsharp, verify against current docs FIRST:



Then:

1. **Match the installed version.** Cross-reference against the version installed in this repo. APIs change across minor versions; do not assume.
2. **Discover provider best practices.** If the task touches a production-sensitive capability, inspect the provider service catalog, official docs, and project docs before choosing an implementation.
3. **Respect explicit direction.** If the user explicitly asks for a specific mechanism, follow it. If project docs clearly mandate a mechanism, follow the project. In both cases, mention the provider-recommended alternative and make the chosen path safe.
4. **Prefer provider-native primitives by default.** If no explicit user/project override exists and the change involves caching, rate limiting, background work, scheduled jobs, shared state, queues, or secrets, use the provider-recommended binding/API. Do not hand-roll an in-memory or polyfill solution that "works" locally but breaks under the provider's execution model — derive the need→native-primitive mapping yourself from this provider's docs.

## Skill Advantage Protocol

Using this skill should produce a meaningfully better result than an unskilled baseline. Apply this loop before and during implementation:

1. **Clarify only when it changes the outcome.** Ask the smallest useful set of questions when the request is ambiguous, preference-heavy, or could change architecture, user-visible behavior, data shape, security posture, analytics, or external side effects. If the safe assumption is obvious, state it and proceed. When asked to surface data that no existing code path captures, state up front the assumption that capture starts now (no backfill) or ask if a backfill source exists — do not silently build net-new storage without surfacing this.
2. **Inspect the nearest real patterns.** Read adjacent files, routes, components, tests, schema, infra, copy, and analytics surfaces before inventing structure. Treat local conventions as the starting point.
3. **Optimize the task's highest-leverage axis.** Identify what would make the result win a review: user-visible correctness, integration quality, accessibility, security, reliability, maintainability, operability, or speed of future change.
4. **Reuse before reimplementing.** Prefer existing components, hooks, helpers, formatting/utility functions, data registries, metadata builders, analytics, pricing, checkout, auth, routing utilities, and API procedures/endpoints/data sources over local one-off clones. Before adding a new API procedure, query, or data fetch, search for one that already returns this data and extend it in place — a surface that fetches data and only logs or partially uses it is a reuse target, not an absent one; never author a parallel endpoint or leave the original orphaned. Before importing for a data fetch, grep the screen for the call it already makes and reuse that exact client/singleton import path and endpoint/procedure name; never create a second client, transport, or parallel endpoint for data an existing call returns, and confirm every imported path and symbol actually exists in the repo before writing it.
5. **Use semantic structures.** Tables, lists, forms, buttons, links, headings, and disclosure controls should use native/project accessible primitives instead of div-only lookalikes.
6. **Prevent drift by construction.** Centralize repeated facts, labels, claims, product defaults, and shared table cells in registries or helpers when multiple surfaces need the same answer.
7. **Synthesize, do not merely comply.** Combine this skill's guidance with repo evidence and the user's goal. When two good approaches exist, borrow the strongest parts of each instead of blindly choosing one.
8. **Check claims against code.** Product copy, docs, and comments must not imply automation, integrations, performance, security, refresh cadence, counts, or data flow that the implementation does not actually provide. Any claim that one component writes, records, updates, calls, or is the source of truth for another is allowed only if the edit performing it is in this same change; before finishing, check each such cross-component claim against the actual edits and downgrade unbacked ones to an explicit TODO or implement them now.
9. **Ship the complete slice.** Include every adjacent artifact needed for the change to be usable and maintainable: wiring, state handling, validation, analytics, tests, docs, migrations, or infra when those surfaces are part of the behavior. When the task shows, displays, or lists user data, deliver the full vertical slice and do not stop at an internal/API/CLI layer: the data-model/schema change AND its migration (a schema change without a migration is incomplete), the path that writes or populates the data, an authenticated endpoint scoped to the current user, and the primary user-facing surface wired through the project's typed data client. Before declaring done, trace one record end-to-end (triggering event → write → read → render); if any hop exists only in a comment or docstring rather than edited code, the slice is NOT done. Shipping only the persistence layer (a schema/migration with no writer, reader, or surface) is an incomplete slice, not a milestone.

## Capability Contract

Use this section when the user prompt touches production risk, even if the prompt does not name this technology explicitly.




Required wiring surfaces:
- provider/runtime configuration discovered during implementation
- nearest typed request/context boundary
- handler/procedure boundary before external side effects

Side-effect barrier:
- Place guards before external APIs, auth mutations, email sends, analytics events, storage writes, and database mutations.


Fallback policy:
- Prefer provider-native/platform-managed primitives by default when no explicit override exists.
- Follow clear user/project overrides, but mention the native alternative and tradeoff.
- Fallbacks must be durable, multi-instance safe, and atomic under concurrency.

Verification rules:
- [error] native-or-explicit-override: Use the provider-native primitive first unless the user/project explicitly overrides it.
- [error] atomic-fallback: Fallback counters must be atomic under concurrency.

## Quick Start

### Existing: Running a Lua Script

```csharp
// AdbCore/Scripting/LuaScriptHost.cs — existing pattern
var host = new LuaScriptHost(context.Log);
var result = host.Run(scriptText, context.Context.Variables, ct);
if (!result.Success)
    return ActionResult.Fail($"Run Lua Script: {result.Error}");
```

### Existing: Adding a New Built-in Module

```csharp
// new code to add — mirrors FsModule/HttpModule/ProcessModule pattern
internal static class MyModule
{
    public static Table Build(Script script, IMyService svc)
    {
        var t = new Table(script);
        t["doThing"] = (Func<string, string>)(input => Guard(() => svc.DoThing(input)));
        return t;
    }

    private static T Guard<T>(Func<T> op)
    {
        try { return op(); }
        catch (ScriptRuntimeException) { throw; }
        catch (Exception ex) { throw new ScriptRuntimeException(ex.Message); }
    }
}
// Then in LuaScriptHost.Run():
script.Globals["mymodule"] = MyModule.Build(script, _myService);
```

## Key Concepts

| Concept | Detail |
|---------|--------|
| `Preset_SoftSandbox` | Disables raw `io`/`os`/`loadfile`; safe for user scripts |
| `vars` table | Seeded from `IDictionary<string, object>`, written back on success |
| `LuaValues` | CLR↔DynValue bridge; bot variables are `string`/`double`/`bool` only |
| `AutoYieldCounter` | Set to 20000 VM instructions; cooperative cancellation for CPU-bound loops |
| `InterpreterException` | Catch all Lua errors; use `DecoratedMessage` for stack context |
| `pcall` | Lua side can wrap risky calls; maps to module `ScriptRuntimeException` |

## Common Patterns

### Vars Round-trip (nil = unset)

```lua
-- Lua script: read, mutate, unset
local x = vars["count"] or 0
vars["count"] = x + 1
vars["obsolete"] = nil  -- removes key from bot variables
```

### json module

```lua
local obj = json.parse('{"key":"value"}')
log(obj["key"])
local encoded = json.encode({ result = 42 })
```

### http module

```lua
local r = http.get("https://example.com/api", { Authorization = "Bearer token" })
if r.status == 200 then
    local data = json.parse(r.body)
end
```

### process module

```lua
local r = process.run("git", {"status", "--short"})
if r.exitCode == 0 then log(r.stdout) end
```

### fs module

```lua
if fs.exists("C:/data/input.txt") then
    local content = fs.read("C:/data/input.txt")
    fs.write("C:/data/output.txt", content)
end
```

## See Also

- [patterns](references/patterns.md)
- [workflows](references/workflows.md)

## Related Skills

- See the **csharp** skill for C# host-side patterns
- See the **dotnet** skill for project/package management
- See the **xunit** skill for testing Lua modules with fake services