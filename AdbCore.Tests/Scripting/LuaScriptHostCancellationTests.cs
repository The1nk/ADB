using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdbCore.Scripting;
using Xunit;

namespace AdbCore.Tests.Scripting;

public class LuaScriptHostCancellationTests
{
    [Fact]
    public async Task RunawayLoop_IsCancelled_WithinTimeout()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(200); // cancel shortly after the loop starts spinning

        var host = new LuaScriptHost(_ => { });
        var run = Task.Run(
            () => host.Run("while true do end", new Dictionary<string, object>(), cts.Token));

        var completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(run, completed); // Run returned (it honored cancellation rather than hanging past 5s)
        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
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
