using System.Collections.Generic;
using AdbCore.Scripting;
using Xunit;

namespace AdbCore.Tests.Scripting;

public class LuaScriptHostTests
{
    private static LuaScriptHost.Result Run(string script, IDictionary<string, object> vars, List<string>? log = null)
        => new LuaScriptHost(s => (log ??= new List<string>()).Add(s)).Run(script, vars, default);

    [Fact]
    public void WritesVariableBack()
    {
        var vars = new Dictionary<string, object>();
        var r = Run("vars.count = 5", vars);
        Assert.True(r.Success);
        Assert.Equal(5d, vars["count"]);
    }

    [Fact]
    public void ReadsExistingVariable_AndCanIncrement()
    {
        var vars = new Dictionary<string, object> { ["i"] = 1d };
        var r = Run("vars.i = vars.i + 1", vars);
        Assert.True(r.Success);
        Assert.Equal(2d, vars["i"]);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var vars = new Dictionary<string, object>();
        var r = Run("local t = json.parse('{\"a\":42}'); vars.a = t.a; vars.s = json.encode({x=1})", vars);
        Assert.True(r.Success);
        Assert.Equal(42d, vars["a"]);
        Assert.Contains("\"x\"", (string)vars["s"]);
    }

    [Fact]
    public void Log_ReachesSink()
    {
        var log = new List<string>();
        var r = Run("log('hello from lua')", new Dictionary<string, object>(), log);
        Assert.True(r.Success);
        Assert.Contains("hello from lua", log);
    }

    [Fact]
    public void LuaError_FailsWithMessage()
    {
        var r = Run("error('boom')", new Dictionary<string, object>());
        Assert.False(r.Success);
        Assert.Contains("boom", r.Error);
    }

    [Fact]
    public void SyntaxError_Fails()
    {
        var r = Run("this is not lua $$", new Dictionary<string, object>());
        Assert.False(r.Success);
    }

    [Fact]
    public void JsonEncode_NonTable_FailsCleanly()
    {
        var r = Run("vars.s = json.encode('not a table')", new Dictionary<string, object>());
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void WritesStringVariableBack()
    {
        var vars = new Dictionary<string, object>();
        var r = Run("vars.name = 'hello'", vars);
        Assert.True(r.Success);
        Assert.Equal("hello", vars["name"]);
    }

    [Fact]
    public void NilAssignment_RemovesVariable()
    {
        var vars = new Dictionary<string, object> { ["gone"] = "here" };
        var r = Run("vars.gone = nil", vars);
        Assert.True(r.Success);
        Assert.False(vars.ContainsKey("gone"));
    }

    [Fact]
    public void CleanScript_Succeeds()
    {
        var r = Run("local x = 1 + 1", new Dictionary<string, object>());
        Assert.True(r.Success);
        Assert.Null(r.Error);
    }
}
