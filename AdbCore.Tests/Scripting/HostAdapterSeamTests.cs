using System.Collections.Generic;
using AdbCore.Scripting;
using Xunit;

namespace AdbCore.Tests.Scripting;

public class HostAdapterSeamTests
{
    [Fact]
    public void LogOnlyConstructor_StillRunsPureScript()
    {
        var vars = new Dictionary<string, object>();
        var r = new LuaScriptHost(_ => { }).Run("vars.x = 1 + 1", vars, default);
        Assert.True(r.Success);
        Assert.Equal(2d, vars["x"]);
    }

    [Fact]
    public void AdapterConstructor_Exists_AndRunsPureScript()
    {
        var host = new LuaScriptHost(_ => { }, new LiveFileSystem(), new LiveProcessRunner(), new HttpRequester());
        var r = host.Run("local x = 1", new Dictionary<string, object>(), default);
        Assert.True(r.Success);
    }
}
