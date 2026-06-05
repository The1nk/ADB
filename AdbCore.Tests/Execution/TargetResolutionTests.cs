using System;
using AdbCore.Android;
using AdbCore.Browser;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Tests.Actions.BuiltIn.Android;
using AdbCore.Tests.Actions.BuiltIn.Browser;
using Xunit;

namespace AdbCore.Tests.Execution;

public class TargetResolutionTests
{
    private static ActionExecutionContext Make(BotExecutionContext ctx, Guid? targetId)
    {
        var action = new BotAction { TargetId = targetId };
        return new ActionExecutionContext(action, ctx, _ => { });
    }

    private static (BotExecutionContext ctx, Guid androidId, Guid windowId) MixedContext()
    {
        var ctx = new BotExecutionContext();
        var androidId = Guid.NewGuid();
        var windowId = Guid.NewGuid();
        ctx.Targets[androidId] = new ResolvedTarget { Handle = NewAndroidDevice() };
        ctx.Targets[windowId] = new ResolvedTarget { Handle = (IntPtr)0x1234 };
        return (ctx, androidId, windowId);
    }

    [Fact]
    public void Unassigned_PicksSingleTargetOfType_Android()
    {
        var (ctx, _, _) = MixedContext();
        var handle = TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, null));
        Assert.NotNull(handle);
    }

    [Fact]
    public void Unassigned_PicksSingleTargetOfType_Window()
    {
        var (ctx, _, _) = MixedContext();
        var hwnd = TargetResolution.ResolveHandle<IntPtr>(Make(ctx, null));
        Assert.Equal((IntPtr)0x1234, hwnd);
    }

    [Fact]
    public void Unassigned_PicksSingleTargetOfType_Browser()
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = NewAndroidDevice() };
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = NewBrowserPage() };
        var page = TargetResolution.ResolveHandle<IBrowserPage>(Make(ctx, null));
        Assert.NotNull(page);
    }

    [Fact]
    public void Unassigned_TwoOfType_IsAmbiguous_ReturnsDefault()
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = NewAndroidDevice() };
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = NewAndroidDevice() };
        Assert.Null(TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, null)));
    }

    [Fact]
    public void Unassigned_NoneOfType_ReturnsDefault()
    {
        var (ctx, _, _) = MixedContext();
        Assert.Null(TargetResolution.ResolveHandle<IBrowserPage>(Make(ctx, null)));
    }

    [Fact]
    public void Explicit_RightType_ResolvesThatTarget()
    {
        var (ctx, androidId, _) = MixedContext();
        Assert.NotNull(TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, androidId)));
    }

    [Fact]
    public void Explicit_WrongType_ReturnsDefault()
    {
        var (ctx, _, windowId) = MixedContext();
        Assert.Null(TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, windowId)));
    }

    [Fact]
    public void Explicit_MissingId_ReturnsDefault()
    {
        var (ctx, _, _) = MixedContext();
        Assert.Null(TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, Guid.NewGuid())));
    }

    [Fact]
    public void Unassigned_SingleTargetTotal_BackCompat()
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[Guid.NewGuid()] = new ResolvedTarget { Handle = NewAndroidDevice() };
        Assert.NotNull(TargetResolution.ResolveHandle<IAndroidDevice>(Make(ctx, null)));
    }

    private static IAndroidDevice NewAndroidDevice() => new FakeAndroidDevice();
    private static IBrowserPage NewBrowserPage() => new FakeBrowserPage();
}
