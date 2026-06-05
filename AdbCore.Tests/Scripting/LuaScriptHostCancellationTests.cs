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
    public void AdapterCancellation_Propagates_AndDoesNotCommitVars()
    {
        var http = new AdbCore.Tests.Scripting.Fakes.FakeHttpRequester
        {
            OnSend = (m, u, b, h) => throw new System.OperationCanceledException()
        };
        var host = new LuaScriptHost(_ => { }, new AdbCore.Tests.Scripting.Fakes.FakeFileSystem(),
            new AdbCore.Tests.Scripting.Fakes.FakeProcessRunner(), http);
        var vars = new Dictionary<string, object> { ["pre"] = "original" };

        // The script mutates `pre` then calls http.get, which throws OCE.
        Assert.Throws<System.OperationCanceledException>(
            () => host.Run("vars.pre = 'changed'; http.get('http://x')", vars, default));

        // Cancellation skipped the write-back: the pre-existing value is untouched (no partial commit).
        Assert.Equal("original", vars["pre"]);
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
