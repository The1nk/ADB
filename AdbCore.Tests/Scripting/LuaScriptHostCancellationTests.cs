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
