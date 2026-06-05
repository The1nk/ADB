using System;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Window;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ActivateWindowActionTests
{
    private sealed class FakeActivator : IWindowActivator
    {
        public IntPtr? Activated { get; private set; }
        public void Activate(IntPtr handle) => Activated = handle;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    [Fact]
    public async Task Activates_TheLoneWindowTarget_AndSucceeds()
    {
        var fake = new FakeActivator();
        var ctx = new BotExecutionContext();
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = (IntPtr)0x4321 };
        var action = new BotAction();   // no explicit TargetId -> resolves the lone Window-handle target

        var r = await new ActivateWindowAction(fake).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(r.Success);
        Assert.Equal("onSuccess", r.OutputPort);
        Assert.Equal((IntPtr)0x4321, fake.Activated);
    }

    [Fact]
    public async Task NoWindowTarget_RoutesOnFailure_AndDoesNotActivate()
    {
        var fake = new FakeActivator();
        var r = await new ActivateWindowAction(fake).ExecuteAsync(Exec(new BotAction(), new BotExecutionContext()), default);
        Assert.False(r.Success);
        Assert.Null(fake.Activated);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new ActivateWindowAction(new FakeActivator());
        Assert.Equal("window.activate", def.TypeKey);
        Assert.Equal("Activate Window", def.DisplayName);
        Assert.Equal("Window", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Empty(def.ConfigFields);
    }
}
