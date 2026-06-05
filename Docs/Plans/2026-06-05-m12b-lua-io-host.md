# M12b — Lua I/O Host Modules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the `http`, `fs`, and `process` host modules to the Lua escape hatch (behind injectable adapters for testability), plus real mid-script cancellation — completing M12 and the dropped M13 (Web & API) + M14 (Files & System).

**Architecture:** Three injectable adapter interfaces (`IFileSystem`, `IProcessRunner`, `IHttpRequester`) sit between the Lua host functions and the real OS. `LuaScriptHost` gains a constructor that accepts the three adapters; the existing `LuaScriptHost(Action<string> log)` convenience constructor wires the live concrete adapters (so `RunLuaScriptAction` is unchanged). Each host module (`fs`/`process`/`http`) is a small focused file that builds its Lua table from an adapter. Operational failures (file missing, transport error, process couldn't start) are raised as `ScriptRuntimeException` so they're `pcall`-able and route to `onFailure`; a result/status (HTTP non-2xx, non-zero exit code) is a returned value, not an error. Cancellation uses a MoonSharp coroutine with `AutoYieldCounter` so even a pure runaway Lua loop is abortable, and the I/O adapters honor the `CancellationToken`.

**Tech Stack:** C# / .NET 10 (net10.0-windows), AdbCore, `MoonSharp` 2.0.0 (already referenced from M12a), `System.Net.Http`, `System.Diagnostics.Process`, `System.IO.File`, xUnit.

**Reference spec:** `Docs/Specs/2026-06-05-m12-lua-scripting-design.md` (§5 host API, §6 cancellation, §7 architecture, §8 M12b, §9 testing). M12a (engine + `vars` + `json` + `log` + the action) is already merged (PR #33).

**Adaptive note:** MoonSharp 2.0.0 specifics — registering variadic/optional-arg callbacks (`DynValue.NewCallback`), building a return `Table`, how a CLR exception thrown inside a callback surfaces (it should be wrapped so the host's `catch (InterpreterException)` sees it — verify and adapt), and the coroutine `AutoYieldCounter` / `Coroutine.Resume()` API for cancellation. **The unit tests (which run real Lua against fake adapters) are the executable spec — adapt the MoonSharp glue until they pass.**

**`<WT>` = the worktree path the controller provides (e.g. `C:\git\ADB\.claude\worktrees\m12b-lua-io-host`). On Windows use the PowerShell tool for `dotnet`/`git` (the Bash tool mangles backslash paths) and NEVER redirect output to `/dev/null` or `NUL`.**

**Merge handling:** M12b has live I/O adapters (real HTTP / file / process) that need a hands-on check → opened as a PR, **user-verified + merged** (NOT self-merged). Unit tests cover all behavior via fakes; the concrete adapters get a live smoke test by the user.

---

## File Structure

- Create `AdbCore/Scripting/IFileSystem.cs` — fs adapter interface.
- Create `AdbCore/Scripting/IProcessRunner.cs` — process adapter interface + `ProcessResult` record.
- Create `AdbCore/Scripting/IHttpRequester.cs` — http adapter interface + `HttpResult` record.
- Create `AdbCore/Scripting/LiveFileSystem.cs` — `System.IO.File`-backed `IFileSystem`.
- Create `AdbCore/Scripting/LiveProcessRunner.cs` — `System.Diagnostics.Process`-backed `IProcessRunner`.
- Create `AdbCore/Scripting/HttpRequester.cs` — shared-`HttpClient`-backed `IHttpRequester`.
- Create `AdbCore/Scripting/Modules/FsModule.cs` — builds the `fs` Lua table from an `IFileSystem`.
- Create `AdbCore/Scripting/Modules/ProcessModule.cs` — builds the `process` Lua table from an `IProcessRunner`.
- Create `AdbCore/Scripting/Modules/HttpModule.cs` — builds the `http` Lua table from an `IHttpRequester`.
- Modify `AdbCore/Scripting/LuaScriptHost.cs` — new adapter-injecting constructor; convenience ctor wires live adapters; register `fs`/`process`/`http`; coroutine-based run with cancellation.
- Tests: `AdbCore.Tests/Scripting/FsModuleTests.cs`, `ProcessModuleTests.cs`, `HttpModuleTests.cs`, `LuaScriptHostCancellationTests.cs`, and shared fakes `AdbCore.Tests/Scripting/Fakes/FakeFileSystem.cs`, `FakeProcessRunner.cs`, `FakeHttpRequester.cs`.

---

## Task 1: Adapter interfaces + host constructor seam

Introduce the three adapter interfaces and let `LuaScriptHost` accept them, defaulting to live implementations so `RunLuaScriptAction` and all M12a tests keep working unchanged. No Lua tables yet — this is the seam.

**Files:** Create `AdbCore/Scripting/IFileSystem.cs`, `IProcessRunner.cs`, `IHttpRequester.cs`; modify `AdbCore/Scripting/LuaScriptHost.cs`; create `AdbCore.Tests/Scripting/HostAdapterSeamTests.cs`.

- [ ] **Step 1: Write the failing test.** Create `AdbCore.Tests/Scripting/HostAdapterSeamTests.cs`:
```csharp
using System.Collections.Generic;
using AdbCore.Scripting;
using Xunit;

namespace AdbCore.Tests.Scripting;

public class HostAdapterSeamTests
{
    // The log-only convenience constructor must still exist and run a pure script (M12a behavior).
    [Fact]
    public void LogOnlyConstructor_StillRunsPureScript()
    {
        var vars = new Dictionary<string, object>();
        var r = new LuaScriptHost(_ => { }).Run("vars.x = 1 + 1", vars, default);
        Assert.True(r.Success);
        Assert.Equal(2d, vars["x"]);
    }

    // The adapter-injecting constructor exists and accepts the three adapters.
    [Fact]
    public void AdapterConstructor_Exists_AndRunsPureScript()
    {
        var host = new LuaScriptHost(_ => { }, new LiveFileSystem(), new LiveProcessRunner(), new HttpRequester());
        var r = host.Run("local x = 1", new Dictionary<string, object>(), default);
        Assert.True(r.Success);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test "<WT>\AdbCore.Tests" --filter "FullyQualifiedName~HostAdapterSeamTests"` → compile FAIL (types/ctor missing).

- [ ] **Step 3: Create the interfaces.**

`AdbCore/Scripting/IFileSystem.cs`:
```csharp
namespace AdbCore.Scripting;

/// <summary>Filesystem operations the Lua `fs` host module needs. Injectable so the module is unit-testable
/// without touching the real disk. Failures throw (the module maps them to Lua errors).</summary>
public interface IFileSystem
{
    string Read(string path);
    void Write(string path, string content);
    void Copy(string source, string destination);
    void Move(string source, string destination);
    bool Exists(string path);
    void Delete(string path);
}
```

`AdbCore/Scripting/IProcessRunner.cs`:
```csharp
namespace AdbCore.Scripting;

/// <summary>The outcome of running an external process: its exit code and captured output.</summary>
public readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Runs an external process to completion. Injectable so the Lua `process` module is unit-testable.
/// A non-zero exit code is a normal <see cref="ProcessResult"/> (not an exception); failure to START the
/// process throws.</summary>
public interface IProcessRunner
{
    ProcessResult Run(string command, IReadOnlyList<string>? arguments, CancellationToken ct);
}
```

`AdbCore/Scripting/IHttpRequester.cs`:
```csharp
namespace AdbCore.Scripting;

/// <summary>An HTTP response: status code, body, and response headers.</summary>
public readonly record struct HttpResult(int Status, string Body, IReadOnlyDictionary<string, string> Headers);

/// <summary>Sends an HTTP request. Injectable so the Lua `http` module is unit-testable without a network.
/// A non-2xx status is a normal <see cref="HttpResult"/> (not an exception); a transport failure throws.</summary>
public interface IHttpRequester
{
    HttpResult Send(string method, string url, string? body, IReadOnlyDictionary<string, string>? headers, CancellationToken ct);
}
```

- [ ] **Step 4: Create the live adapters** (so the convenience ctor can default to them; the Lua wiring comes in Tasks 2-4, but the concrete types must exist now).

`AdbCore/Scripting/LiveFileSystem.cs`:
```csharp
using System.IO;

namespace AdbCore.Scripting;

/// <summary>The real <see cref="IFileSystem"/> backed by <see cref="System.IO.File"/>.</summary>
public sealed class LiveFileSystem : IFileSystem
{
    public string Read(string path) => File.ReadAllText(path);
    public void Write(string path, string content) => File.WriteAllText(path, content);
    public void Copy(string source, string destination) => File.Copy(source, destination, overwrite: true);
    public void Move(string source, string destination) => File.Move(source, destination, overwrite: true);
    public bool Exists(string path) => File.Exists(path);
    public void Delete(string path) => File.Delete(path);
}
```

`AdbCore/Scripting/LiveProcessRunner.cs`:
```csharp
using System.Diagnostics;

namespace AdbCore.Scripting;

/// <summary>The real <see cref="IProcessRunner"/> backed by <see cref="System.Diagnostics.Process"/>. Runs the
/// process to completion (honoring the token, killing the process tree if cancelled) and captures stdout/stderr.</summary>
public sealed class LiveProcessRunner : IProcessRunner
{
    public ProcessResult Run(string command, IReadOnlyList<string>? arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (arguments is not null)
            foreach (var a in arguments)
                psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start process '{command}'.");

        // Read both streams to avoid a full-pipe deadlock, then wait (cancellable).
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            process.WaitForExit(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
```

`AdbCore/Scripting/HttpRequester.cs`:
```csharp
using System.Net.Http;

namespace AdbCore.Scripting;

/// <summary>The real <see cref="IHttpRequester"/> backed by a shared <see cref="HttpClient"/> (one per process,
/// per the HttpClient guidance). Blocks synchronously so the Lua host functions stay simple, honoring the token.</summary>
public sealed class HttpRequester : IHttpRequester
{
    private static readonly HttpClient Client = new();

    public HttpResult Send(string method, string url, string? body, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null)
            request.Content = new StringContent(body);
        if (headers is not null)
            foreach (var h in headers)
                request.Headers.TryAddWithoutValidation(h.Key, h.Value);

        using var response = Client.Send(request, ct);
        var responseBody = response.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
        var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers)
            responseHeaders[h.Key] = string.Join(", ", h.Value);
        foreach (var h in response.Content.Headers)
            responseHeaders[h.Key] = string.Join(", ", h.Value);

        return new HttpResult((int)response.StatusCode, responseBody, responseHeaders);
    }
}
```
**Adapt if needed:** `HttpClient.Send` (synchronous) exists on .NET 5+; if a `StringContent` overwrites a caller `Content-Type` header you set via `headers`, that's acceptable for v1. The unit tests use a fake `IHttpRequester`, so the concrete `HttpRequester` is validated by the user's live smoke test, not unit tests — keep it simple and correct.

- [ ] **Step 5: Add the adapter-injecting constructor to `LuaScriptHost`.** Modify `AdbCore/Scripting/LuaScriptHost.cs`:
  - Add three readonly fields `_fs`, `_process`, `_http`.
  - Change the existing `public LuaScriptHost(Action<string> log)` to chain: `: this(log, new LiveFileSystem(), new LiveProcessRunner(), new HttpRequester())`.
  - Add `public LuaScriptHost(Action<string> log, IFileSystem fileSystem, IProcessRunner processRunner, IHttpRequester httpRequester)` that null-guards all four and stores them.
  - Do NOT register the new tables yet (Tasks 2-4 do that). The body of `Run` is unchanged for now.
```csharp
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
```

- [ ] **Step 6: Run to verify it passes** — `dotnet test "<WT>\AdbCore.Tests" --filter "FullyQualifiedName~HostAdapterSeamTests"` → PASS. Also re-run the M12a host tests to confirm no regression: `--filter "FullyQualifiedName~LuaScriptHostTests"` → still green.

- [ ] **Step 7: Commit.**
```
git -C "<WT>" add AdbCore/Scripting/IFileSystem.cs AdbCore/Scripting/IProcessRunner.cs AdbCore/Scripting/IHttpRequester.cs AdbCore/Scripting/LiveFileSystem.cs AdbCore/Scripting/LiveProcessRunner.cs AdbCore/Scripting/HttpRequester.cs AdbCore/Scripting/LuaScriptHost.cs AdbCore.Tests/Scripting/HostAdapterSeamTests.cs
git -C "<WT>" commit -m "feat(scripting): adapter interfaces + live impls + host injection seam (M12b)"
```

---

## Task 2: `fs` host module

Build the `fs` Lua table from an `IFileSystem`. Operational failures become `ScriptRuntimeException` (pcall-able, route to onFailure).

**Files:** Create `AdbCore/Scripting/Modules/FsModule.cs`; modify `LuaScriptHost.cs` (register `fs`); create `AdbCore.Tests/Scripting/Fakes/FakeFileSystem.cs`, `AdbCore.Tests/Scripting/FsModuleTests.cs`.

- [ ] **Step 1: Create the fake.** `AdbCore.Tests/Scripting/Fakes/FakeFileSystem.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using AdbCore.Scripting;

namespace AdbCore.Tests.Scripting.Fakes;

/// <summary>In-memory <see cref="IFileSystem"/> for tests. Read of a missing path throws (like the real one).</summary>
public sealed class FakeFileSystem : IFileSystem
{
    public Dictionary<string, string> Files { get; } = new();

    public string Read(string path) => Files.TryGetValue(path, out var c) ? c : throw new FileNotFoundException(path);
    public void Write(string path, string content) => Files[path] = content;
    public void Copy(string source, string destination) => Files[destination] = Read(source);
    public void Move(string source, string destination) { Files[destination] = Read(source); Files.Remove(source); }
    public bool Exists(string path) => Files.ContainsKey(path);
    public void Delete(string path) => Files.Remove(path);
}
```

- [ ] **Step 2: Write the failing tests.** `AdbCore.Tests/Scripting/FsModuleTests.cs`:
```csharp
using System.Collections.Generic;
using AdbCore.Scripting;
using AdbCore.Tests.Scripting.Fakes;
using Xunit;

namespace AdbCore.Tests.Scripting;

public class FsModuleTests
{
    private static LuaScriptHost.Result Run(string script, FakeFileSystem fs, IDictionary<string, object> vars)
        => new LuaScriptHost(_ => { }, fs, new FakeProcessRunner(), new FakeHttpRequester()).Run(script, vars, default);

    [Fact]
    public void Write_ThenRead_RoundTrips()
    {
        var fs = new FakeFileSystem();
        var vars = new Dictionary<string, object>();
        var r = Run("fs.write('a.txt', 'hello'); vars.c = fs.read('a.txt')", fs, vars);
        Assert.True(r.Success);
        Assert.Equal("hello", vars["c"]);
        Assert.Equal("hello", fs.Files["a.txt"]);
    }

    [Fact]
    public void Exists_ReturnsBool()
    {
        var fs = new FakeFileSystem();
        fs.Files["there.txt"] = "x";
        var vars = new Dictionary<string, object>();
        var r = Run("vars.a = fs.exists('there.txt'); vars.b = fs.exists('nope.txt')", fs, vars);
        Assert.True(r.Success);
        Assert.Equal(true, vars["a"]);
        Assert.Equal(false, vars["b"]);
    }

    [Fact]
    public void Copy_Move_Delete_Work()
    {
        var fs = new FakeFileSystem();
        fs.Files["src.txt"] = "data";
        var r = Run("fs.copy('src.txt','copy.txt'); fs.move('copy.txt','moved.txt'); fs.delete('src.txt')", fs, new Dictionary<string, object>());
        Assert.True(r.Success);
        Assert.False(fs.Files.ContainsKey("src.txt"));
        Assert.False(fs.Files.ContainsKey("copy.txt"));
        Assert.Equal("data", fs.Files["moved.txt"]);
    }

    [Fact]
    public void Read_Missing_RoutesToFailure_AndIsPcallable()
    {
        // Unprotected read of a missing file fails the run.
        var r1 = Run("vars.c = fs.read('missing.txt')", new FakeFileSystem(), new Dictionary<string, object>());
        Assert.False(r1.Success);

        // Wrapped in pcall, the script can recover and succeed.
        var vars = new Dictionary<string, object>();
        var r2 = Run("local ok = pcall(function() fs.read('missing.txt') end); vars.ok = ok", new FakeFileSystem(), vars);
        Assert.True(r2.Success);
        Assert.Equal(false, vars["ok"]);
    }
}
```
(`FakeProcessRunner` and `FakeHttpRequester` are created in Tasks 3 and 4. To keep Task 2 self-contained and compiling, ALSO create minimal throwing stubs now under `AdbCore.Tests/Scripting/Fakes/` — see note below — and flesh them out in their tasks.)

**Compile note:** so `FsModuleTests` compiles before Tasks 3-4, create stub fakes now:
`AdbCore.Tests/Scripting/Fakes/FakeProcessRunner.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using AdbCore.Scripting;

namespace AdbCore.Tests.Scripting.Fakes;

/// <summary>Configurable fake process runner. Default returns exit 0 / empty output.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public Func<string, IReadOnlyList<string>?, ProcessResult> OnRun { get; set; } = (_, _) => new ProcessResult(0, "", "");
    public ProcessResult Run(string command, IReadOnlyList<string>? arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return OnRun(command, arguments);
    }
}
```
`AdbCore.Tests/Scripting/Fakes/FakeHttpRequester.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using AdbCore.Scripting;

namespace AdbCore.Tests.Scripting.Fakes;

/// <summary>Configurable fake http requester. Default returns 200 / empty body / no headers.</summary>
public sealed class FakeHttpRequester : IHttpRequester
{
    public Func<string, string, string?, IReadOnlyDictionary<string, string>?, HttpResult> OnSend { get; set; }
        = (_, _, _, _) => new HttpResult(200, "", new Dictionary<string, string>());
    public HttpResult Send(string method, string url, string? body, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return OnSend(method, url, body, headers);
    }
}
```

- [ ] **Step 3: Run to verify it fails** — `dotnet test "<WT>\AdbCore.Tests" --filter "FullyQualifiedName~FsModuleTests"` → compile FAIL (no `FsModule`, `fs` not registered).

- [ ] **Step 4: Create `AdbCore/Scripting/Modules/FsModule.cs`** (adapt the MoonSharp glue until the tests pass):
```csharp
using MoonSharp.Interpreter;

namespace AdbCore.Scripting.Modules;

/// <summary>Builds the Lua <c>fs</c> table over an <see cref="IFileSystem"/>. Operational failures
/// (missing file, IO error) surface as <see cref="ScriptRuntimeException"/> so a script can <c>pcall</c>
/// them or let them route to onFailure.</summary>
internal static class FsModule
{
    public static Table Build(Script script, IFileSystem fs)
    {
        var t = new Table(script);
        t["read"] = (Func<string, string>)(p => Guard(() => fs.Read(p)));
        t["write"] = (Action<string, string>)((p, c) => Guard(() => { fs.Write(p, c); return 0; }));
        t["copy"] = (Action<string, string>)((s, d) => Guard(() => { fs.Copy(s, d); return 0; }));
        t["move"] = (Action<string, string>)((s, d) => Guard(() => { fs.Move(s, d); return 0; }));
        t["exists"] = (Func<string, bool>)(p => Guard(() => fs.Exists(p)));
        t["delete"] = (Action<string>)(p => Guard(() => { fs.Delete(p); return 0; }));
        return t;
    }

    /// <summary>Runs an adapter call, converting any CLR exception into a Lua-catchable script error.</summary>
    private static T Guard<T>(Func<T> op)
    {
        try { return op(); }
        catch (ScriptRuntimeException) { throw; }
        catch (Exception ex) { throw new ScriptRuntimeException(ex.Message); }
    }
}
```
**Adaptation:** if assigning `Action<string,string>` directly to a Lua table slot doesn't register cleanly in MoonSharp 2.0.0, use `DynValue.NewCallback(...)`. The tests are the spec — especially `Read_Missing_RoutesToFailure_AndIsPcallable`, which requires the thrown `ScriptRuntimeException` to be both `pcall`-able in Lua AND (when unprotected) caught by the host's `catch (InterpreterException)`. Verify that a CLR exception thrown inside a MoonSharp callback surfaces as an `InterpreterException`; if MoonSharp lets a raw CLR exception escape `DoString`, the `Guard` wrapper (throwing `ScriptRuntimeException`, which IS an `InterpreterException`) is exactly what prevents that — confirm via the test.

- [ ] **Step 5: Register `fs` in `LuaScriptHost.Run`.** After the `log` registration line, add:
```csharp
        script.Globals["fs"] = AdbCore.Scripting.Modules.FsModule.Build(script, _fs);
```
(Add `using AdbCore.Scripting.Modules;` and shorten if preferred.)

- [ ] **Step 6: Run to verify it passes** — `--filter "FullyQualifiedName~FsModuleTests"` → 4 pass. Iterate glue until green.

- [ ] **Step 7: Commit.**
```
git -C "<WT>" add AdbCore/Scripting/Modules/FsModule.cs AdbCore/Scripting/LuaScriptHost.cs AdbCore.Tests/Scripting/Fakes/FakeFileSystem.cs AdbCore.Tests/Scripting/Fakes/FakeProcessRunner.cs AdbCore.Tests/Scripting/Fakes/FakeHttpRequester.cs AdbCore.Tests/Scripting/FsModuleTests.cs
git -C "<WT>" commit -m "feat(scripting): fs host module (read/write/copy/move/exists/delete)"
```

---

## Task 3: `process` host module

Build the `process` Lua table from an `IProcessRunner`. `process.run(command [, argsTable]) -> { exitCode, stdout, stderr }`. Non-zero exit is a value; failure-to-start throws (pcall-able).

**Files:** Create `AdbCore/Scripting/Modules/ProcessModule.cs`; modify `LuaScriptHost.cs` (register `process`); create `AdbCore.Tests/Scripting/ProcessModuleTests.cs`. (`FakeProcessRunner` already created in Task 2.)

- [ ] **Step 1: Write the failing tests.** `AdbCore.Tests/Scripting/ProcessModuleTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using AdbCore.Scripting;
using AdbCore.Tests.Scripting.Fakes;
using Xunit;

namespace AdbCore.Tests.Scripting;

public class ProcessModuleTests
{
    private static LuaScriptHost.Result Run(string script, FakeProcessRunner proc, IDictionary<string, object> vars)
        => new LuaScriptHost(_ => { }, new FakeFileSystem(), proc, new FakeHttpRequester()).Run(script, vars, default);

    [Fact]
    public void Run_ReturnsExitCodeAndOutput()
    {
        var proc = new FakeProcessRunner { OnRun = (cmd, args) => new ProcessResult(0, "the-out", "the-err") };
        var vars = new Dictionary<string, object>();
        var r = Run("local p = process.run('git'); vars.code = p.exitCode; vars.out = p.stdout; vars.err = p.stderr", proc, vars);
        Assert.True(r.Success);
        Assert.Equal(0d, vars["code"]);
        Assert.Equal("the-out", vars["out"]);
        Assert.Equal("the-err", vars["err"]);
    }

    [Fact]
    public void Run_PassesArgsTable()
    {
        IReadOnlyList<string>? captured = null;
        var proc = new FakeProcessRunner { OnRun = (cmd, args) => { captured = args; return new ProcessResult(0, "", ""); } };
        var r = Run("process.run('git', {'status', '--short'})", proc, new Dictionary<string, object>());
        Assert.True(r.Success);
        Assert.NotNull(captured);
        Assert.Equal(new[] { "status", "--short" }, captured);
    }

    [Fact]
    public void Run_NonZeroExit_IsAValueNotAnError()
    {
        var proc = new FakeProcessRunner { OnRun = (cmd, args) => new ProcessResult(2, "", "boom") };
        var vars = new Dictionary<string, object>();
        var r = Run("local p = process.run('x'); vars.code = p.exitCode", proc, vars);
        Assert.True(r.Success);            // non-zero exit does NOT fail the run
        Assert.Equal(2d, vars["code"]);
    }

    [Fact]
    public void Run_StartFailure_RoutesToFailure()
    {
        var proc = new FakeProcessRunner { OnRun = (cmd, args) => throw new InvalidOperationException("cannot start") };
        var r = Run("process.run('bogus')", proc, new Dictionary<string, object>());
        Assert.False(r.Success);
        Assert.Contains("cannot start", r.Error);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~ProcessModuleTests"` → compile/run FAIL.

- [ ] **Step 3: Create `AdbCore/Scripting/Modules/ProcessModule.cs`:**
```csharp
using MoonSharp.Interpreter;

namespace AdbCore.Scripting.Modules;

/// <summary>Builds the Lua <c>process</c> table over an <see cref="IProcessRunner"/>. Returns a result table
/// <c>{ exitCode, stdout, stderr }</c>. A non-zero exit code is a value; failure to start the process surfaces
/// as a <see cref="ScriptRuntimeException"/> (pcall-able / routes to onFailure). Honors the CancellationToken.</summary>
internal static class ProcessModule
{
    public static Table Build(Script script, IProcessRunner runner, CancellationToken ct)
    {
        var t = new Table(script);
        t["run"] = DynValue.NewCallback((ctx, args) =>
        {
            var command = args.AsType(0, "process.run", DataType.String).String;
            IReadOnlyList<string>? argList = null;
            if (args.Count > 1 && args[1].Type == DataType.Table)
            {
                var list = new List<string>();
                foreach (var v in args[1].Table.Values)
                    list.Add(v.CastToString());
                argList = list;
            }

            ProcessResult result;
            try { result = runner.Run(command, argList, ct); }
            catch (ScriptRuntimeException) { throw; }
            catch (OperationCanceledException) { throw; } // cancellation is not a script failure
            catch (Exception ex) { throw new ScriptRuntimeException(ex.Message); }

            var ret = new Table(script);
            ret["exitCode"] = result.ExitCode;
            ret["stdout"] = result.StdOut;
            ret["stderr"] = result.StdErr;
            return DynValue.NewTable(ret);
        });
        return t;
    }
}
```
**Adaptation:** confirm `args.AsType`, `args[i].Table.Values`, `CastToString()`, and `DynValue.NewCallback((ctx, args) => ...)` signatures against MoonSharp 2.0.0; adapt until the 4 tests pass. The arg-table iteration must preserve order (`status` before `--short`) — if `Table.Values` order isn't guaranteed, iterate `1..Table.Length` via `Table.Get(i)`.

- [ ] **Step 4: Register `process` in `LuaScriptHost.Run`** (pass `ct`):
```csharp
        script.Globals["process"] = AdbCore.Scripting.Modules.ProcessModule.Build(script, _process, ct);
```

- [ ] **Step 5: Run to verify it passes** — `--filter "FullyQualifiedName~ProcessModuleTests"` → 4 pass.

- [ ] **Step 6: Commit.**
```
git -C "<WT>" add AdbCore/Scripting/Modules/ProcessModule.cs AdbCore/Scripting/LuaScriptHost.cs AdbCore.Tests/Scripting/ProcessModuleTests.cs
git -C "<WT>" commit -m "feat(scripting): process host module (run -> exitCode/stdout/stderr)"
```

---

## Task 4: `http` host module

Build the `http` Lua table from an `IHttpRequester`. `http.get(url [, headers])` / `http.post(url, body [, headers])` -> `{ status, body, headers }`. Non-2xx is a value; transport failure throws (pcall-able).

**Files:** Create `AdbCore/Scripting/Modules/HttpModule.cs`; modify `LuaScriptHost.cs` (register `http`); create `AdbCore.Tests/Scripting/HttpModuleTests.cs`. (`FakeHttpRequester` already created in Task 2.)

- [ ] **Step 1: Write the failing tests.** `AdbCore.Tests/Scripting/HttpModuleTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using AdbCore.Scripting;
using AdbCore.Tests.Scripting.Fakes;
using Xunit;

namespace AdbCore.Tests.Scripting;

public class HttpModuleTests
{
    private static LuaScriptHost.Result Run(string script, FakeHttpRequester http, IDictionary<string, object> vars)
        => new LuaScriptHost(_ => { }, new FakeFileSystem(), new FakeProcessRunner(), http).Run(script, vars, default);

    [Fact]
    public void Get_ReturnsStatusBodyHeaders()
    {
        var http = new FakeHttpRequester
        {
            OnSend = (m, url, body, h) => new HttpResult(200, "{\"ok\":true}",
                new Dictionary<string, string> { ["Content-Type"] = "application/json" })
        };
        var vars = new Dictionary<string, object>();
        var r = Run("local res = http.get('http://x'); vars.s = res.status; vars.b = res.body; vars.ct = res.headers['Content-Type']", http, vars);
        Assert.True(r.Success);
        Assert.Equal(200d, vars["s"]);
        Assert.Equal("{\"ok\":true}", vars["b"]);
        Assert.Equal("application/json", vars["ct"]);
    }

    [Fact]
    public void Get_SendsMethodAndUrl()
    {
        string? method = null, url = null;
        var http = new FakeHttpRequester { OnSend = (m, u, body, h) => { method = m; url = u; return new HttpResult(200, "", new Dictionary<string, string>()); } };
        Run("http.get('http://example.com/a')", http, new Dictionary<string, object>());
        Assert.Equal("GET", method);
        Assert.Equal("http://example.com/a", url);
    }

    [Fact]
    public void Post_SendsBodyAndHeaders()
    {
        string? sentBody = null;
        IReadOnlyDictionary<string, string>? sentHeaders = null;
        var http = new FakeHttpRequester { OnSend = (m, u, body, h) => { sentBody = body; sentHeaders = h; return new HttpResult(201, "", new Dictionary<string, string>()); } };
        var vars = new Dictionary<string, object>();
        var r = Run("local res = http.post('http://x', 'payload', {Authorization='Bearer t'}); vars.s = res.status", http, vars);
        Assert.True(r.Success);
        Assert.Equal("payload", sentBody);
        Assert.NotNull(sentHeaders);
        Assert.Equal("Bearer t", sentHeaders!["Authorization"]);
        Assert.Equal(201d, vars["s"]);
    }

    [Fact]
    public void Non2xx_IsAValueNotAnError()
    {
        var http = new FakeHttpRequester { OnSend = (m, u, body, h) => new HttpResult(404, "nope", new Dictionary<string, string>()) };
        var vars = new Dictionary<string, object>();
        var r = Run("local res = http.get('http://x'); vars.s = res.status", http, vars);
        Assert.True(r.Success);
        Assert.Equal(404d, vars["s"]);
    }

    [Fact]
    public void TransportFailure_RoutesToFailure()
    {
        var http = new FakeHttpRequester { OnSend = (m, u, body, h) => throw new InvalidOperationException("dns fail") };
        var r = Run("http.get('http://x')", http, new Dictionary<string, object>());
        Assert.False(r.Success);
        Assert.Contains("dns fail", r.Error);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~HttpModuleTests"` → FAIL.

- [ ] **Step 3: Create `AdbCore/Scripting/Modules/HttpModule.cs`:**
```csharp
using MoonSharp.Interpreter;

namespace AdbCore.Scripting.Modules;

/// <summary>Builds the Lua <c>http</c> table over an <see cref="IHttpRequester"/>. <c>get</c>/<c>post</c> return
/// a result table <c>{ status, body, headers }</c>. A non-2xx status is a value; a transport failure surfaces as
/// a <see cref="ScriptRuntimeException"/> (pcall-able / routes to onFailure). Honors the CancellationToken.</summary>
internal static class HttpModule
{
    public static Table Build(Script script, IHttpRequester http, CancellationToken ct)
    {
        var t = new Table(script);
        t["get"] = DynValue.NewCallback((ctx, args) =>
        {
            var url = args.AsType(0, "http.get", DataType.String).String;
            var headers = HeadersFrom(args, 1);
            return Send(script, http, "GET", url, null, headers, ct);
        });
        t["post"] = DynValue.NewCallback((ctx, args) =>
        {
            var url = args.AsType(0, "http.post", DataType.String).String;
            var body = args.Count > 1 && !args[1].IsNil() ? args[1].CastToString() : null;
            var headers = HeadersFrom(args, 2);
            return Send(script, http, "POST", url, body, headers, ct);
        });
        return t;
    }

    private static IReadOnlyDictionary<string, string>? HeadersFrom(CallbackArguments args, int index)
    {
        if (args.Count <= index || args[index].Type != DataType.Table) return null;
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in args[index].Table.Pairs)
            if (pair.Key.Type == DataType.String)
                d[pair.Key.String] = pair.Value.CastToString();
        return d;
    }

    private static DynValue Send(Script script, IHttpRequester http, string method, string url,
        string? body, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        HttpResult result;
        try { result = http.Send(method, url, body, headers, ct); }
        catch (ScriptRuntimeException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new ScriptRuntimeException(ex.Message); }

        var ret = new Table(script);
        ret["status"] = result.Status;
        ret["body"] = result.Body;
        var h = new Table(script);
        foreach (var kv in result.Headers)
            h[kv.Key] = kv.Value;
        ret["headers"] = h;
        return DynValue.NewTable(ret);
    }
}
```
**Adaptation:** confirm `CallbackArguments`, `args.AsType`, `args[i].CastToString()`, `Table.Pairs` against MoonSharp 2.0.0. The headers lookup `res.headers['Content-Type']` must work — MoonSharp table keys are case-sensitive, so the Lua-side header table is keyed by the exact header name the adapter returns (the test uses `'Content-Type'` to match). Adapt until the 5 tests pass.

- [ ] **Step 4: Register `http` in `LuaScriptHost.Run`** (pass `ct`):
```csharp
        script.Globals["http"] = AdbCore.Scripting.Modules.HttpModule.Build(script, _http, ct);
```

- [ ] **Step 5: Run to verify it passes** — `--filter "FullyQualifiedName~HttpModuleTests"` → 5 pass.

- [ ] **Step 6: Commit.**
```
git -C "<WT>" add AdbCore/Scripting/Modules/HttpModule.cs AdbCore/Scripting/LuaScriptHost.cs AdbCore.Tests/Scripting/HttpModuleTests.cs
git -C "<WT>" commit -m "feat(scripting): http host module (get/post -> status/body/headers)"
```

---

## Task 5: Real mid-script cancellation (coroutine AutoYieldCounter)

M12a only checked the token before the run. Now make a runaway pure-Lua loop (and any long script) abortable by running the script as a MoonSharp coroutine that auto-yields every N instructions; between yields, check the token. The I/O adapters already honor `ct` (Tasks 1/3/4) for the blocking-call case; this covers the CPU-bound case.

**Files:** Modify `AdbCore/Scripting/LuaScriptHost.cs`; create `AdbCore.Tests/Scripting/LuaScriptHostCancellationTests.cs`.

- [ ] **Step 1: Write the failing test.** `AdbCore.Tests/Scripting/LuaScriptHostCancellationTests.cs`:
```csharp
using System.Collections.Generic;
using System.Threading;
using AdbCore.Scripting;
using Xunit;

namespace AdbCore.Tests.Scripting;

public class LuaScriptHostCancellationTests
{
    [Fact]
    public void RunawayLoop_IsCancelled_WithinTimeout()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(200); // cancel shortly after the loop starts spinning

        var host = new LuaScriptHost(_ => { });
        // A pure CPU loop with no I/O. Without cooperative cancellation this never returns.
        var task = System.Threading.Tasks.Task.Run(() =>
            Assert.Throws<System.OperationCanceledException>(() =>
                host.Run("while true do end", new Dictionary<string, object>(), cts.Token)));

        Assert.True(task.Wait(System.TimeSpan.FromSeconds(5)), "Run did not honor cancellation within 5s");
    }

    [Fact]
    public void NotCancelled_CleanScriptStillSucceeds()
    {
        var host = new LuaScriptHost(_ => { });
        var vars = new Dictionary<string, object> { ["i"] = 0d };
        var r = host.Run("for k=1,1000 do vars.i = vars.i + 1 end", vars, default);
        Assert.True(r.Success);
        Assert.Equal(1000d, vars["i"]);
    }
}
```
**Design decision (matches M12a):** a cancelled run throws `OperationCanceledException` out of `Run` (it is NOT a `Result(false, ...)` — cancellation is not a script "failure"). `RunLuaScriptAction.ExecuteAsync` already calls `ct.ThrowIfCancellationRequested()` and the engine treats a thrown `OperationCanceledException` as a stop, so this is consistent. If a different propagation proves necessary for the engine's Stop handling, adapt — but keep the two tests above passing.

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~LuaScriptHostCancellationTests"` → the runaway-loop test hangs/fails (no cooperative cancellation yet). (Use a per-test timeout; do not let the suite hang — the test itself bounds the wait to 5s and fails if exceeded.)

- [ ] **Step 3: Convert `Run` to a cancellable coroutine.** In `LuaScriptHost.Run`, replace the `try { script.DoString(scriptText); } catch (InterpreterException ...)` block with a coroutine loop:
```csharp
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
```
**Adaptation (MoonSharp 2.0.0 — tests are the spec):**
- Confirm `Script.LoadString` returns a callable `DynValue` (a function), `Script.CreateCoroutine(DynValue)` returns a `DynValue` whose `.Coroutine` exposes `AutoYieldCounter` (int) and `Resume(...)`, and that an auto-yield surfaces as `DataType.YieldRequest`. If the exact members differ, find the equivalent in the installed package (e.g. `coroutine.AutoYieldCounter`, `Coroutine.Resume()`), and adapt. The REQUIREMENT: a pure `while true do end` must be interrupted by the token within seconds, and a normal bounded loop must still run to completion and write `vars` back.
- A `SyntaxErrorException` from `LoadString` is an `InterpreterException` → still caught → `Result(false, ...)`. Confirm `error('boom')` raised inside the coroutine still surfaces as a `ScriptRuntimeException` out of `Resume()` (caught) — re-run `LuaScriptHostTests` (M12a) to confirm the 10 existing tests STILL PASS with the coroutine execution path. This is critical: the coroutine refactor must not regress json/log/vars/error behavior.
- Tune `AutoYieldCounter` (e.g. 20000) so the runaway-loop test cancels well under its 5s bound without throttling normal scripts. Report the value chosen.
- If MoonSharp's coroutine path interferes with `vars` write-back (it shouldn't — globals persist on the `Script`), verify via `NotCancelled_CleanScriptStillSucceeds`.

- [ ] **Step 4: Run to verify it passes** — both cancellation tests pass AND `--filter "FullyQualifiedName~LuaScriptHostTests"` (the 10 M12a tests) STILL pass AND the fs/process/http module tests still pass. Run the whole `AdbCore.Tests` scripting set: `--filter "FullyQualifiedName~Scripting"`.

- [ ] **Step 5: Commit.**
```
git -C "<WT>" add AdbCore/Scripting/LuaScriptHost.cs AdbCore.Tests/Scripting/LuaScriptHostCancellationTests.cs
git -C "<WT>" commit -m "feat(scripting): cancellable script execution via coroutine auto-yield"
```

---

## Task 6: Build + test sweep + PR (user-verified)

- [ ] **Step 1:** `dotnet build "<WT>\ADB.slnx" -v q --nologo` → 0 warnings, 0 errors.
- [ ] **Step 2:** `dotnet test "<WT>\ADB.slnx"` → all pass. Report totals (AdbCore.Tests gains: HostAdapterSeam +2, FsModule +4, ProcessModule +4, HttpModule +5, Cancellation +2; no registry-count changes — M12b adds NO new actions, only host capabilities inside the existing `scripting.runLua`).
- [ ] **Step 3:** Confirm no registry/palette count change is needed (M12b adds no actions). If any count test fails, something registered an action it shouldn't have — investigate.
- [ ] **Step 4:** Push the branch and open a PR summarizing the host API (`http`/`fs`/`process`), the error model (status/exit = value; operational failure = pcall-able Lua error), and cancellation. **This slice has live I/O adapters → do NOT self-merge.** Provide the user a short live-verify script they can paste into a Run Lua Script node, e.g.:
  ```lua
  -- live smoke test
  fs.write('adb_lua_test.txt', 'hello from lua')
  log('exists: ' .. tostring(fs.exists('adb_lua_test.txt')))
  log('read: ' .. fs.read('adb_lua_test.txt'))
  local res = http.get('https://example.com')
  log('http status: ' .. res.status)
  local p = process.run('cmd', {'/c', 'echo', 'hi'})
  log('proc exit: ' .. p.exitCode .. ' out: ' .. p.stdout)
  fs.delete('adb_lua_test.txt')
  ```
  Report the PR URL and await the user's live verification + merge.

---

## Self-Review Notes (addressed)

- **Spec coverage (§5 host API):** `http` get/post → {status,body,headers} (Task 4); `json` already in M12a; `fs` read/write/copy/move/exists/delete (Task 2); `process.run` → {exitCode,stdout,stderr} (Task 3); `log` already in M12a. ✓
- **Spec §5 error model:** status/exit-code returned as a value; transport/fs/start failures raise a `ScriptRuntimeException` (pcall-able, routes to onFailure) via the `Guard`/try-catch wrappers in each module. Tests assert both halves (non-2xx/non-zero = value; failure = `r.Success == false` AND pcall recovers). ✓
- **Spec §6 cancellation:** coroutine `AutoYieldCounter` interrupts CPU-bound scripts; adapters honor `ct` for blocking I/O; `OperationCanceledException` propagates (not a Result failure), consistent with M12a + the action/engine. Task 5. ✓
- **Spec §7 architecture:** `IFileSystem`/`IProcessRunner`/`IHttpRequester` injectable adapters; concrete `LiveFileSystem`/`LiveProcessRunner`/`HttpRequester`; convenience ctor wires live, full ctor injects fakes; modules are small focused files. ✓
- **Spec §8/§11 merge:** user-verified PR (live I/O), not self-merge. Task 6. ✓
- **Type consistency:** `IFileSystem`(Read/Write/Copy/Move/Exists/Delete), `IProcessRunner.Run → ProcessResult(ExitCode,StdOut,StdErr)`, `IHttpRequester.Send → HttpResult(Status,Body,Headers)`, `LuaScriptHost(log, fs, proc, http)`, modules `FsModule/ProcessModule/HttpModule.Build(...)`. The action (`RunLuaScriptAction`) is UNCHANGED — it still calls `new LuaScriptHost(context.Log)`, which now transparently wires live adapters. ✓
- **No new external NuGet deps** beyond what M12a added (MoonSharp); `System.Net.Http`/`Process`/`File` are in the BCL. ✓
- **Adaptive points flagged:** MoonSharp callback registration, `CallbackArguments`/`AsType`/`CastToString`/`Table.Pairs`, CLR-exception-in-callback surfacing, and the coroutine `AutoYieldCounter`/`Resume`/`YieldRequest` API — tests are the spec.
