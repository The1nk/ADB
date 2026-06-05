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
        var r1 = Run("vars.c = fs.read('missing.txt')", new FakeFileSystem(), new Dictionary<string, object>());
        Assert.False(r1.Success);

        var vars = new Dictionary<string, object>();
        var r2 = Run("local ok = pcall(function() fs.read('missing.txt') end); vars.ok = ok", new FakeFileSystem(), vars);
        Assert.True(r2.Success);
        Assert.Equal(false, vars["ok"]);
    }
}
