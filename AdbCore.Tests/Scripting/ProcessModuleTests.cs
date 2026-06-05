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
        Assert.True(r.Success);
        Assert.Equal(2d, vars["code"]);
    }

    [Fact]
    public void Run_NonTableArgs_RoutesToFailure()
    {
        var proc = new FakeProcessRunner();
        var r = Run("process.run('git', 'status')", proc, new Dictionary<string, object>());
        Assert.False(r.Success);
        Assert.Contains("must be a table", r.Error);
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
