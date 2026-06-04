using System.Collections.Generic;
using System.Drawing;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Ocr;
using AdbCore.Screen;
using AdbCore.Tests.Ocr;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class ScreenOcrActionTests
{
    private static BotExecutionContext WindowCtx(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = handle };
        return ctx;
    }
    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });
    private static OcrResult Result(params OcrWord[] w) => new(string.Join(" ", System.Array.ConvertAll(w, x => x.Text)), w);

    [Fact]
    public async Task ReadText_WritesRecognizedTextToVar()
    {
        var id = Guid.NewGuid();
        var ctx = WindowCtx(id, (IntPtr)5);
        var action = new BotAction { TargetId = id };
        var read = new ReadTextAction(new FakeWindowCapture(400, 300), new FakeOcrEngine(Result(new OcrWord("Score", new Rectangle(0, 0, 9, 9), 0.9))));

        var r = await read.ExecuteAsync(Exec(action, ctx), default);

        Assert.True(r.Success);
        Assert.Equal("Score", ctx.Variables["text"]);
    }

    [Fact]
    public async Task FindText_Match_WritesMatchVariables_AndSuccess()
    {
        var id = Guid.NewGuid();
        var ctx = WindowCtx(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { ["text"] = "attack" } };
        var find = new FindTextAction(new FakeWindowCapture(400, 300), new FakeOcrEngine(Result(new OcrWord("ATTACK", new Rectangle(100, 40, 30, 20), 0.9))), new FixedRandomSource(123));

        var r = await find.ExecuteAsync(Exec(action, ctx), default);

        Assert.True(r.Success);
        Assert.Equal("onSuccess", r.OutputPort);
        Assert.Equal("100", ctx.Variables["matchLeft"]);
        Assert.Equal("130", ctx.Variables["matchRight"]);
        Assert.Equal("123", ctx.Variables["matchRandX"]);
    }

    [Fact]
    public async Task FindText_NoMatch_Fails()
    {
        var id = Guid.NewGuid();
        var ctx = WindowCtx(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { ["text"] = "attack" } };
        var find = new FindTextAction(new FakeWindowCapture(400, 300), new FakeOcrEngine(Result(new OcrWord("settings", new Rectangle(0, 0, 9, 9), 0.9))), new FixedRandomSource(0));

        var r = await find.ExecuteAsync(Exec(action, ctx), default);

        Assert.False(r.Success);
    }

    [Fact]
    public async Task ReadText_NoWindow_Fails()
    {
        var read = new ReadTextAction(new FakeWindowCapture(10, 10), new FakeOcrEngine(Result()));
        var r = await read.ExecuteAsync(Exec(new BotAction(), new BotExecutionContext()), default);
        Assert.False(r.Success);
        Assert.Contains("Window", r.ErrorMessage);
    }
}
