using AdbCore.Actions.BuiltIn.Browser;
using AdbCore.Execution;
using AdbCore.Models;

namespace AdbCore.Tests.Actions.BuiltIn.Browser;

public class BrowserQueryActionTests
{
    private static (ActionExecutionContext ctx, FakeBrowserPage page) WithPage(BotAction action)
    {
        var page = new FakeBrowserPage();
        var ctx = new BotExecutionContext();
        var id = action.TargetId ?? Guid.NewGuid();
        action.TargetId = id;
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Browser, Selector = "browser:chromium", Handle = page };
        return (new ActionExecutionContext(action, ctx, _ => { }), page);
    }

    [Fact]
    public async Task WaitForSelector_WaitsWithTimeout()
    {
        var action = new BotAction { Config = { ["selector"] = ".ready", ["timeoutMs"] = 5000 } };
        var (ctx, page) = WithPage(action);

        await new WaitForSelectorAction().ExecuteAsync(ctx, default);

        Assert.Equal("wait .ready 5000", page.Calls.Single());
    }

    [Fact]
    public async Task GetText_WritesResultVariable()
    {
        var action = new BotAction { Config = { ["selector"] = "h1", ["resultVar"] = "title" } };
        var (ctx, page) = WithPage(action);
        page.TextResult = "Welcome";

        var r = await new GetTextAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("gettext h1", page.Calls.Single());
        Assert.Equal("Welcome", ctx.Context.Variables["title"]);
    }

    [Fact]
    public async Task GetText_DefaultResultVar_IsText()
    {
        var action = new BotAction { Config = { ["selector"] = "h1" } };
        var (ctx, page) = WithPage(action);
        page.TextResult = "Hi";

        await new GetTextAction().ExecuteAsync(ctx, default);

        Assert.Equal("Hi", ctx.Context.Variables["text"]);
    }
}
