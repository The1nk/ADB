using System.Drawing;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Ocr;
using AdbCore.Screen;
using AdbCore.Tests.Ocr;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidOcrActionTests
{
    private static (ActionExecutionContext ctx, FakeAndroidDevice dev) WithDevice(BotAction action, int w = 1080, int h = 1920)
    {
        var dev = new FakeAndroidDevice { ScreenshotBytes = AndroidImageActionBaseTests.PngBytes(w, h) };
        var ctx = new BotExecutionContext();
        var id = action.TargetId ?? Guid.NewGuid();
        action.TargetId = id;
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:x", Handle = dev };
        return (new ActionExecutionContext(action, ctx, _ => { }), dev);
    }

    private static OcrResult Result(params OcrWord[] w) => new(string.Join(" ", System.Array.ConvertAll(w, x => x.Text)), w);

    [Fact]
    public async Task ReadText_WritesRecognizedTextToVar()
    {
        var action = new BotAction();
        var (ctx, _) = WithDevice(action);
        var read = new AndroidReadTextAction(new FakeOcrEngine(Result(new OcrWord("Score", new Rectangle(0, 0, 9, 9), 0.9))));

        var r = await read.ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("Score", ctx.Context.Variables["text"]);
    }

    [Fact]
    public async Task FindText_Match_WritesMatchVariables_AndSuccess()
    {
        var action = new BotAction { Config = { ["text"] = "attack" } };
        var (ctx, _) = WithDevice(action);
        var find = new AndroidFindTextAction(new FakeOcrEngine(Result(new OcrWord("ATTACK", new Rectangle(100, 40, 30, 20), 0.9))), new FixedRandomSource(123));

        var r = await find.ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("onSuccess", r.OutputPort);
        Assert.Equal("100", ctx.Context.Variables["matchLeft"]);
        Assert.Equal("130", ctx.Context.Variables["matchRight"]);
        Assert.Equal("123", ctx.Context.Variables["matchRandX"]);
    }

    [Fact]
    public async Task FindText_NoMatch_Fails()
    {
        var action = new BotAction { Config = { ["text"] = "attack" } };
        var (ctx, _) = WithDevice(action);
        var find = new AndroidFindTextAction(new FakeOcrEngine(Result(new OcrWord("settings", new Rectangle(0, 0, 9, 9), 0.9))), new FixedRandomSource(0));

        Assert.False((await find.ExecuteAsync(ctx, default)).Success);
    }

    [Fact]
    public async Task ReadText_NoDevice_Fails()
    {
        var exec = new ActionExecutionContext(new BotAction(), new BotExecutionContext(), _ => { });
        var r = await new AndroidReadTextAction(new FakeOcrEngine(Result())).ExecuteAsync(exec, default);
        Assert.False(r.Success);
        Assert.Contains("Android", r.ErrorMessage);
    }

    [Fact]
    public void Find_Definition_Metadata()
    {
        var def = new AndroidFindTextAction(new FakeOcrEngine(Result()), new FixedRandomSource(0));
        Assert.Equal("android.findText", def.TypeKey);
        Assert.Equal("Find Text (Android)", def.DisplayName);
        Assert.Equal("Android", def.Category);
    }

    [Fact]
    public async Task WaitForText_Present_Succeeds()
    {
        var action = new BotAction { Config = { ["text"] = "ready", [AndroidWaitForTextAction.TimeoutMsKey] = 1000, [AndroidWaitForTextAction.PollIntervalMsKey] = 10 } };
        var (ctx, _) = WithDevice(action);
        var wait = new AndroidWaitForTextAction(new FakeOcrEngine(Result(new OcrWord("READY", new Rectangle(1, 2, 3, 4), 0.9))), new FixedRandomSource(0));

        Assert.True((await wait.ExecuteAsync(ctx, default)).Success);
    }

    [Fact]
    public async Task WaitForText_Timeout_Fails()
    {
        var action = new BotAction { Config = { ["text"] = "ready", [AndroidWaitForTextAction.TimeoutMsKey] = 30, [AndroidWaitForTextAction.PollIntervalMsKey] = 10 } };
        var (ctx, _) = WithDevice(action);
        var wait = new AndroidWaitForTextAction(new FakeOcrEngine(Result(new OcrWord("loading", new Rectangle(1, 2, 3, 4), 0.9))), new FixedRandomSource(0));

        var r = await wait.ExecuteAsync(ctx, default);
        Assert.False(r.Success);
        Assert.Contains("did not appear", r.ErrorMessage);
    }

    [Fact]
    public async Task AssertTextAbsent_Absent_Ok_Present_Fail()
    {
        var a1 = new BotAction { Config = { ["text"] = "gameover" } };
        var (c1, _) = WithDevice(a1);
        Assert.True((await new AndroidAssertTextAbsentAction(new FakeOcrEngine(Result(new OcrWord("menu", new Rectangle(1, 2, 3, 4), 0.9)))).ExecuteAsync(c1, default)).Success);

        var a2 = new BotAction { Config = { ["text"] = "gameover" } };
        var (c2, _) = WithDevice(a2);
        Assert.False((await new AndroidAssertTextAbsentAction(new FakeOcrEngine(Result(new OcrWord("gameover", new Rectangle(1, 2, 3, 4), 0.9)))).ExecuteAsync(c2, default)).Success);
    }
}
