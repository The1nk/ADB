using System.IO;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidAppActionTests
{
    private static (ActionExecutionContext ctx, FakeAndroidDevice dev) WithDevice(BotAction action)
    {
        var dev = new FakeAndroidDevice();
        var ctx = new BotExecutionContext();
        var id = action.TargetId ?? Guid.NewGuid();
        action.TargetId = id;
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:x", Handle = dev };
        return (new ActionExecutionContext(action, ctx, _ => { }), dev);
    }

    [Fact]
    public async Task LaunchApp_CallsDeviceWithPackage()
    {
        var action = new BotAction { Config = { ["package"] = "com.example.app" } };
        var (ctx, dev) = WithDevice(action);

        await new LaunchAppAction().ExecuteAsync(ctx, default);

        Assert.Equal("launch com.example.app", dev.Calls.Single());
    }

    [Fact]
    public async Task InstallApk_CallsDeviceWithPath()
    {
        var action = new BotAction { Config = { ["apkPath"] = @"C:\app.apk" } };
        var (ctx, dev) = WithDevice(action);

        await new InstallApkAction().ExecuteAsync(ctx, default);

        Assert.Equal(@"install C:\app.apk", dev.Calls.Single());
    }

    [Fact]
    public async Task Screenshot_WritesDeviceBytesToPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"adbshot_{Guid.NewGuid():N}.png");
        var action = new BotAction { Config = { ["outputPath"] = path } };
        var (ctx, dev) = WithDevice(action);
        dev.ScreenshotBytes = new byte[] { 1, 2, 3, 4 };
        try
        {
            var r = await new AndroidScreenshotAction().ExecuteAsync(ctx, default);

            Assert.True(r.Success);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Screenshot_NoPath_Fails()
    {
        var action = new BotAction { Config = { } };
        var (ctx, _) = WithDevice(action);

        var r = await new AndroidScreenshotAction().ExecuteAsync(ctx, default);

        Assert.False(r.Success);
    }
}
