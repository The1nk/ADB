using AdbCore.Actions.BuiltIn.Browser;
using AdbCore.Execution;
using AdbCore.Models;

namespace AdbCore.Tests.Actions.BuiltIn.Browser;

public class BrowserNavActionTests
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
    public async Task OpenUrl_NavigatesPage()
    {
        var action = new BotAction { Config = { ["url"] = "https://example.com" } };
        var (ctx, page) = WithPage(action);

        var r = await new OpenUrlAction().ExecuteAsync(ctx, default);

        Assert.True(r.Success);
        Assert.Equal("goto https://example.com", page.Calls.Single());
    }

    [Fact]
    public async Task Click_ClicksSelector()
    {
        var action = new BotAction { Config = { ["selector"] = "#submit" } };
        var (ctx, page) = WithPage(action);

        await new BrowserClickAction().ExecuteAsync(ctx, default);

        Assert.Equal("click #submit", page.Calls.Single());
    }

    [Fact]
    public async Task Type_FillsSelector()
    {
        var action = new BotAction { Config = { ["selector"] = "#name", ["text"] = "Ada" } };
        var (ctx, page) = WithPage(action);

        await new BrowserTypeAction().ExecuteAsync(ctx, default);

        Assert.Equal("type #name Ada", page.Calls.Single());
    }

    [Fact]
    public async Task NoBrowserBound_Fails()
    {
        var ctx = new BotExecutionContext();
        var exec = new ActionExecutionContext(new BotAction { Config = { ["url"] = "https://x" } }, ctx, _ => { });

        var r = await new OpenUrlAction().ExecuteAsync(exec, default);

        Assert.False(r.Success);
        Assert.Contains("Browser", r.ErrorMessage);
    }
}
