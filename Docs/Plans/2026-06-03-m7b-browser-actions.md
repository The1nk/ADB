# M7b — Browser Action Category (Playwright) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Browser action category end-to-end: an `IBrowserPage` adapter (over Playwright), a launch-owned `BrowserTargetBinder` that resolves `browser:<engine>` to a live page, five Browser actions, run-end disposal so the browser closes cleanly, and a browser-engine dropdown in the Test Run target picker.

**Architecture:** Mirrors M7a's handle-as-bound-adapter pattern: the bound `IBrowserPage` is stored as `ResolvedTarget.Handle`; action executors read it from the handle and `await` the page op (no constructor injection). Tested units (actions, base, selector) use a fake page; the concrete Playwright adapter + binder are live-verified (needs `playwright install`). The runner disposes `IAsyncDisposable`/`IDisposable` handles at run end.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), Microsoft.Playwright, xUnit, WPF. Per `Docs/Specs/2026-06-03-m7-android-browser-design.md` §2, §4.

---

## File Structure

**AdbCore (new):**
- `Browser/IBrowserPage.cs` — the async page adapter (the handle type).
- `Browser/BrowserSelector.cs` — `browser:<engine>` parse + the engine list (tested).
- `Browser/PlaywrightBrowserPage.cs` — concrete adapter (live), `IAsyncDisposable`.
- `Actions/BuiltIn/Browser/BrowserActionBase.cs` + `OpenUrlAction.cs`, `BrowserClickAction.cs`, `BrowserTypeAction.cs`, `WaitForSelectorAction.cs`, `GetTextAction.cs`.

**AdbCore (modified):** `Actions/BuiltIn/BuiltInActions.cs` — register the five.

**BotRunner (new/modified):** `BrowserTargetBinder.cs`; `RunnerApp.cs` — wire the async binder + run-end disposal.

**BotBuilder (modified):** `Core/Integration/TargetSelectionRow.cs` (`IsBrowser`); `TargetPickerDialog.xaml(.cs)` — engine dropdown.

**Tests (AdbCore.Tests):** `Actions/BuiltIn/Browser/*Tests.cs`, `Browser/BrowserSelectorTests.cs`, a `FakeBrowserPage`, registration-count bumps.

---

## Task 1: Playwright package + IBrowserPage + selector

**Files:**
- Modify: `AdbCore/AdbCore.csproj`
- Create: `AdbCore/Browser/IBrowserPage.cs`, `AdbCore/Browser/BrowserSelector.cs`
- Test: `AdbCore.Tests/Browser/BrowserSelectorTests.cs`

- [ ] **Step 1: Add the Microsoft.Playwright package:**

```bash
dotnet add AdbCore/AdbCore.csproj package Microsoft.Playwright
```

Expected: latest stable version added + restores. (Browser *binaries* are installed separately by the user via `playwright install` — not needed to build.)

- [ ] **Step 2: Write the failing selector test** — `AdbCore.Tests/Browser/BrowserSelectorTests.cs`:

```csharp
using AdbCore.Browser;

namespace AdbCore.Tests.Browser;

public class BrowserSelectorTests
{
    [Theory]
    [InlineData("browser:firefox", "firefox")]
    [InlineData("browser:WEBKIT", "webkit")]
    [InlineData("browser:", "chromium")]      // no engine -> default chromium
    public void ParseEngine_ReturnsEngine(string selector, string expected)
        => Assert.Equal(expected, BrowserSelector.ParseEngine(selector));

    [Theory]
    [InlineData("url:https://x")]
    [InlineData("chromium")]
    public void ParseEngine_NonBrowser_ReturnsNull(string selector)
        => Assert.Null(BrowserSelector.ParseEngine(selector));

    [Fact]
    public void Engines_AreTheThreePlaywrightEngines()
        => Assert.Equal(new[] { "chromium", "firefox", "webkit" }, BrowserSelector.Engines);
}
```

- [ ] **Step 3: Run to verify it fails** — `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~BrowserSelectorTests"` → FAIL (type missing).

- [ ] **Step 4: Create the types.**

`AdbCore/Browser/IBrowserPage.cs`:

```csharp
namespace AdbCore.Browser;

/// <summary>A live browser page, bound to a Playwright-launched browser. Stored as the
/// <c>ResolvedTarget.Handle</c> for Browser targets; the Browser actions await it.</summary>
public interface IBrowserPage
{
    Task GotoAsync(string url);
    Task ClickAsync(string selector);

    /// <summary>Sets the value of the element matched by <paramref name="selector"/>.</summary>
    Task TypeAsync(string selector, string text);

    Task WaitForSelectorAsync(string selector, int timeoutMs);

    /// <summary>The visible text of the element matched by <paramref name="selector"/>.</summary>
    Task<string> GetTextAsync(string selector);
}
```

`AdbCore/Browser/BrowserSelector.cs`:

```csharp
namespace AdbCore.Browser;

/// <summary>Parses a Browser target selector of the form <c>browser:&lt;engine&gt;</c> (chromium / firefox /
/// webkit). Playwright launches its own browser, so the selector names the engine (not an external context).</summary>
public static class BrowserSelector
{
    private const string Prefix = "browser:";

    /// <summary>The Playwright engines the picker offers.</summary>
    public static IReadOnlyList<string> Engines { get; } = new[] { "chromium", "firefox", "webkit" };

    /// <summary>The engine from a <c>browser:&lt;engine&gt;</c> selector (empty engine -> chromium), or null
    /// when the selector isn't a browser selector.</summary>
    public static string? ParseEngine(string selector)
    {
        if (!selector.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var engine = selector[Prefix.Length..].Trim().ToLowerInvariant();
        return engine.Length == 0 ? "chromium" : engine;
    }
}
```

- [ ] **Step 5: Run to verify it passes** — same filter → PASS (6 cases). Then `dotnet build AdbCore/AdbCore.csproj -c Debug --nologo` → 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/AdbCore.csproj AdbCore/Browser/IBrowserPage.cs AdbCore/Browser/BrowserSelector.cs AdbCore.Tests/Browser/BrowserSelectorTests.cs
git commit -m "feat(browser): add Microsoft.Playwright + IBrowserPage + BrowserSelector"
```

---

## Task 2: BrowserActionBase + Open URL / Click / Type

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Browser/BrowserActionBase.cs`, `OpenUrlAction.cs`, `BrowserClickAction.cs`, `BrowserTypeAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Browser/FakeBrowserPage.cs`, `BrowserNavActionTests.cs`

- [ ] **Step 1: Write the fake + failing tests.**

`AdbCore.Tests/Actions/BuiltIn/Browser/FakeBrowserPage.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using AdbCore.Browser;

namespace AdbCore.Tests.Actions.BuiltIn.Browser;

internal sealed class FakeBrowserPage : IBrowserPage
{
    public List<string> Calls { get; } = new();
    public string TextResult { get; set; } = string.Empty;

    public Task GotoAsync(string url) { Calls.Add($"goto {url}"); return Task.CompletedTask; }
    public Task ClickAsync(string selector) { Calls.Add($"click {selector}"); return Task.CompletedTask; }
    public Task TypeAsync(string selector, string text) { Calls.Add($"type {selector} {text}"); return Task.CompletedTask; }
    public Task WaitForSelectorAsync(string selector, int timeoutMs) { Calls.Add($"wait {selector} {timeoutMs}"); return Task.CompletedTask; }
    public Task<string> GetTextAsync(string selector) { Calls.Add($"gettext {selector}"); return Task.FromResult(TextResult); }
}
```

`AdbCore.Tests/Actions/BuiltIn/Browser/BrowserNavActionTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~BrowserNavActionTests"` → FAIL.

- [ ] **Step 3: Create `BrowserActionBase`** — `AdbCore/Actions/BuiltIn/Browser/BrowserActionBase.cs`:

```csharp
using System.Linq;
using AdbCore.Browser;
using AdbCore.Execution;
using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Shared base for Browser actions: resolves the action's target to the bound
/// <see cref="IBrowserPage"/> handle, and exposes the standard success/failure ports.</summary>
public abstract class BrowserActionBase : IActionDefinition, IActionExecutor
{
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    public abstract string TypeKey { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public string Category => "Browser";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public virtual List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };
    public abstract List<ConfigField> ConfigFields { get; }
    public virtual bool SupportsRetry => false;

    public abstract Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct);

    /// <summary>The bound browser page for this action's target (explicit TargetId, or the sole target when
    /// unset); null when the target isn't a bound Browser page.</summary>
    protected static IBrowserPage? ResolvePage(ActionExecutionContext context)
    {
        var targets = context.Context.Targets;
        ResolvedTarget? target = context.Action.TargetId is Guid id
            ? targets.TryGetValue(id, out var t) ? t : null
            : targets.Count == 1 ? targets.Values.First() : null;
        return target?.Handle as IBrowserPage;
    }

    protected ActionResult RequiresPage() => ActionResult.Fail($"{DisplayName} requires a Browser target.");
}
```

- [ ] **Step 4: Create the three actions.**

`AdbCore/Actions/BuiltIn/Browser/OpenUrlAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Navigates the browser to a URL.</summary>
public sealed class OpenUrlAction : BrowserActionBase
{
    public override string TypeKey => "browser.openUrl";
    public override string DisplayName => "Open URL";
    public override string Description => "Navigates the browser page to a URL.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "url", Label = "URL", Type = ConfigFieldType.String, DefaultValue = "" },
    };

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolvePage(context) is not { } page)
        {
            return RequiresPage();
        }

        var url = ConfigValues.GetString(context.Action.Config, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            return ActionResult.Fail("Open URL: a URL is required.");
        }

        await page.GotoAsync(url);
        return ActionResult.Ok(SuccessPort);
    }
}
```

`AdbCore/Actions/BuiltIn/Browser/BrowserClickAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Clicks the element matched by a selector.</summary>
public sealed class BrowserClickAction : BrowserActionBase
{
    public override string TypeKey => "browser.click";
    public override string DisplayName => "Click Element";
    public override string Description => "Clicks the element matched by a CSS/text selector.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "selector", Label = "Selector", Type = ConfigFieldType.String, DefaultValue = "" },
    };

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolvePage(context) is not { } page)
        {
            return RequiresPage();
        }

        var selector = ConfigValues.GetString(context.Action.Config, "selector");
        if (string.IsNullOrWhiteSpace(selector))
        {
            return ActionResult.Fail("Click Element: a selector is required.");
        }

        await page.ClickAsync(selector);
        return ActionResult.Ok(SuccessPort);
    }
}
```

`AdbCore/Actions/BuiltIn/Browser/BrowserTypeAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Types text into the element matched by a selector.</summary>
public sealed class BrowserTypeAction : BrowserActionBase
{
    public override string TypeKey => "browser.type";
    public override string DisplayName => "Type";
    public override string Description => "Sets the value of the element matched by a selector.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "selector", Label = "Selector", Type = ConfigFieldType.String, DefaultValue = "" },
        new ConfigField { Key = "text", Label = "Text", Type = ConfigFieldType.String, DefaultValue = "" },
    };

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolvePage(context) is not { } page)
        {
            return RequiresPage();
        }

        var selector = ConfigValues.GetString(context.Action.Config, "selector");
        if (string.IsNullOrWhiteSpace(selector))
        {
            return ActionResult.Fail("Type: a selector is required.");
        }

        await page.TypeAsync(selector, ConfigValues.GetString(context.Action.Config, "text"));
        return ActionResult.Ok(SuccessPort);
    }
}
```

- [ ] **Step 5: Register the three** in `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, after the Android block
  (the `Add(new AndroidScreenshotAction(), ...)` line); add `using AdbCore.Actions.BuiltIn.Browser;` at the top:

```csharp
        // Browser (handle-based — the bound IBrowserPage is the ResolvedTarget handle; no injection).
        Add(new OpenUrlAction(), definitions, executors);
        Add(new BrowserClickAction(), definitions, executors);
        Add(new BrowserTypeAction(), definitions, executors);
```

- [ ] **Step 6: Run to verify the Browser tests pass** — filter `~BrowserNavActionTests` → PASS (4 tests). NOTE: the registration-count test `BuiltInActionsTests` and `PaletteViewModelTests` will now FAIL (counts changed) — EXPECTED, fixed in Task 3 once all five Browser actions are registered. Do NOT change the count tests here.

- [ ] **Step 7: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Browser/BrowserActionBase.cs AdbCore/Actions/BuiltIn/Browser/OpenUrlAction.cs AdbCore/Actions/BuiltIn/Browser/BrowserClickAction.cs AdbCore/Actions/BuiltIn/Browser/BrowserTypeAction.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/Browser/FakeBrowserPage.cs AdbCore.Tests/Actions/BuiltIn/Browser/BrowserNavActionTests.cs
git commit -m "feat(browser): BrowserActionBase + Open URL / Click / Type actions"
```

---

## Task 3: Wait for Selector / Get Text + registration counts

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Browser/WaitForSelectorAction.cs`, `GetTextAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/Browser/BrowserQueryActionTests.cs`; update `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` and any other count test.

- [ ] **Step 1: Write the failing tests** — `AdbCore.Tests/Actions/BuiltIn/Browser/BrowserQueryActionTests.cs`:

```csharp
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
        Assert.Equal("Welcome", ctx.Variables["title"]);
    }

    [Fact]
    public async Task GetText_DefaultResultVar_IsText()
    {
        var action = new BotAction { Config = { ["selector"] = "h1" } };
        var (ctx, page) = WithPage(action);
        page.TextResult = "Hi";

        await new GetTextAction().ExecuteAsync(ctx, default);

        Assert.Equal("Hi", ctx.Variables["text"]);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — filter `~BrowserQueryActionTests` → FAIL.

- [ ] **Step 3: Create the two actions.**

`AdbCore/Actions/BuiltIn/Browser/WaitForSelectorAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Waits for an element matching a selector to appear.</summary>
public sealed class WaitForSelectorAction : BrowserActionBase
{
    public override string TypeKey => "browser.waitForSelector";
    public override string DisplayName => "Wait for Selector";
    public override string Description => "Waits up to a timeout for an element to appear.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "selector", Label = "Selector", Type = ConfigFieldType.String, DefaultValue = "" },
        new ConfigField { Key = "timeoutMs", Label = "Timeout (ms)", Type = ConfigFieldType.Number, DefaultValue = 30000 },
    };

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolvePage(context) is not { } page)
        {
            return RequiresPage();
        }

        var selector = ConfigValues.GetString(context.Action.Config, "selector");
        if (string.IsNullOrWhiteSpace(selector))
        {
            return ActionResult.Fail("Wait for Selector: a selector is required.");
        }

        await page.WaitForSelectorAsync(selector, ConfigValues.GetInt(context.Action.Config, "timeoutMs", 30000));
        return ActionResult.Ok(SuccessPort);
    }
}
```

`AdbCore/Actions/BuiltIn/Browser/GetTextAction.cs`:

```csharp
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Reads the text of an element into a run variable.</summary>
public sealed class GetTextAction : BrowserActionBase
{
    public const string DefaultResultVar = "text";

    public override string TypeKey => "browser.getText";
    public override string DisplayName => "Get Text";
    public override string Description => "Reads the visible text of an element into a variable.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "selector", Label = "Selector", Type = ConfigFieldType.String, DefaultValue = "" },
        new ConfigField { Key = "resultVar", Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar },
    };

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolvePage(context) is not { } page)
        {
            return RequiresPage();
        }

        var selector = ConfigValues.GetString(context.Action.Config, "selector");
        if (string.IsNullOrWhiteSpace(selector))
        {
            return ActionResult.Fail("Get Text: a selector is required.");
        }

        var resultVar = ConfigValues.GetString(context.Action.Config, "resultVar");
        if (string.IsNullOrWhiteSpace(resultVar))
        {
            resultVar = DefaultResultVar;
        }

        context.Context.Variables[resultVar] = await page.GetTextAsync(selector);
        return ActionResult.Ok(SuccessPort);
    }
}
```

- [ ] **Step 4: Register the two** in `BuiltInActions.cs`, after the `Add(new BrowserTypeAction(), ...)` line:

```csharp
        Add(new WaitForSelectorAction(), definitions, executors);
        Add(new GetTextAction(), definitions, executors);
```

- [ ] **Step 5: Update the registration-count assertions.** In `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`,
  the assertions currently read `Assert.Equal(26, defs.Count);` and `Assert.Equal(23, execs.Count);` (after
  M7a's Android). The five Browser actions add five defs and five execs, so change them to:

```csharp
        Assert.Equal(31, defs.Count);
        Assert.Equal(28, execs.Count);
```

- [ ] **Step 6: Run targeted, then the FULL suite.**
  - `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~BrowserQueryActionTests|FullyQualifiedName~BuiltInActionsTests"` → PASS.
  - Then `dotnet test ADB.slnx -c Debug --nologo` (whole suite). Fix any OTHER count/category assertion that
    now fails (e.g. `BotBuilder.Core.Tests/PaletteViewModelTests.cs` counts actions — it was 26 after M7a;
    update to 31 and the category comment to include `+ 5 Browser`). Re-run until the whole suite is green.

- [ ] **Step 7: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Browser/WaitForSelectorAction.cs AdbCore/Actions/BuiltIn/Browser/GetTextAction.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/Browser/BrowserQueryActionTests.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs
git commit -m "feat(browser): Wait for Selector / Get Text actions (Browser category complete)"
```
(If you updated other count tests, `git add` them too and mention them.)

---

## Task 4: PlaywrightBrowserPage adapter + BrowserTargetBinder + disposal (live)

Implements the concrete Playwright adapter, the launch-owned binder, and run-end disposal. **No unit
tests** (needs installed browsers); verified live. Playwright's API is stable — adjust only option-class
names if the installed version differs.

**Files:**
- Create: `AdbCore/Browser/PlaywrightBrowserPage.cs`, `BotRunner/BrowserTargetBinder.cs`
- Modify: `BotRunner/RunnerApp.cs`

- [ ] **Step 1: Implement `PlaywrightBrowserPage`** — `AdbCore/Browser/PlaywrightBrowserPage.cs`. It owns the
  `IPlaywright` + `IBrowser` + `IPage` and disposes them. (Adjust `BrowserTypeLaunchOptions` /
  `PageWaitForSelectorOptions` names to the installed Microsoft.Playwright if needed.)

```csharp
using Microsoft.Playwright;

namespace AdbCore.Browser;

/// <summary>An <see cref="IBrowserPage"/> backed by a Playwright-launched browser. Launch-owned: it starts
/// its own browser engine and closes it on dispose. Verified live (requires installed browsers —
/// `playwright install`).</summary>
public sealed class PlaywrightBrowserPage : IBrowserPage, IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IPage _page;

    private PlaywrightBrowserPage(IPlaywright playwright, IBrowser browser, IPage page)
    {
        _playwright = playwright;
        _browser = browser;
        _page = page;
    }

    /// <summary>Launches the given engine (chromium / firefox / webkit) and opens a page.</summary>
    public static async Task<PlaywrightBrowserPage> LaunchAsync(string engine, bool headless)
    {
        var playwright = await Playwright.CreateAsync();
        var browserType = playwright[engine]; // indexer: "chromium" / "firefox" / "webkit"
        var browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions { Headless = headless });
        var page = await browser.NewPageAsync();
        return new PlaywrightBrowserPage(playwright, browser, page);
    }

    public Task GotoAsync(string url) => _page.GotoAsync(url);                       // Task<IResponse?> is-a Task
    public Task ClickAsync(string selector) => _page.ClickAsync(selector);
    public Task TypeAsync(string selector, string text) => _page.FillAsync(selector, text);
    public Task WaitForSelectorAsync(string selector, int timeoutMs)
        => _page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions { Timeout = timeoutMs });
    public Task<string> GetTextAsync(string selector) => _page.InnerTextAsync(selector);

    public async ValueTask DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }
}
```

- [ ] **Step 2: Create `BrowserTargetBinder`** — `BotRunner/BrowserTargetBinder.cs` (async; mirrors the
  Android binder). For each Browser target, parse the engine, launch a `PlaywrightBrowserPage`, store it as
  the handle. An invalid engine or a launch failure (browsers not installed) → `CommandLineException`
  (exit 2) with a README-pointing message. Skip when there are no Browser targets.

```csharp
using AdbCore.Browser;
using AdbCore.Execution;
using AdbCore.Models;

namespace BotRunner;

/// <summary>At run start, launches a Playwright browser per Browser target and stores the bound
/// <see cref="IBrowserPage"/> as the handle. A bad engine / missing browsers is a CLI usage error (exit 2).</summary>
public static class BrowserTargetBinder
{
    public static async Task BindAsync(IReadOnlyDictionary<Guid, ResolvedTarget> targets)
    {
        foreach (var target in targets.Values.Where(t => t.Type == BotTargetType.Browser))
        {
            var engine = BrowserSelector.ParseEngine(target.Selector)
                ?? throw new CommandLineException($"Browser target selector '{target.Selector}' must be 'browser:<engine>'.");

            if (!BrowserSelector.Engines.Contains(engine))
            {
                throw new CommandLineException(
                    $"Unknown browser engine '{engine}'. Use chromium, firefox, or webkit.");
            }

            try
            {
                target.Handle = await PlaywrightBrowserPage.LaunchAsync(engine, headless: false);
            }
            catch (Exception ex)
            {
                throw new CommandLineException(
                    $"Could not launch the '{engine}' browser (have you run `playwright install`? see README): {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 3: Wire the binder + run-end disposal in `BotRunner/RunnerApp.cs`.** Read `RunAsync` first. Make
  these changes:
  (a) After `AndroidTargetBinder.Bind(resolvedTargets);`, add `await BrowserTargetBinder.BindAsync(resolvedTargets);`.
  (b) Wrap the run so handles are always disposed at the end. The simplest safe shape: put the binders +
  logger setup + execution inside a `try`, and a `finally` that disposes handles. Concretely, change the
  body from (after `var resolvedTargets = TargetResolver.Resolve(...)`):

```csharp
        try
        {
            WindowTargetBinder.Bind(resolvedTargets, new Win32WindowResolver());
            AndroidTargetBinder.Bind(resolvedTargets);
            await BrowserTargetBinder.BindAsync(resolvedTargets);

            var logPath = args.LogFile ?? Path.ChangeExtension(args.BotPath, ".log");
            using var fileWriter = new StreamWriter(logPath, append: false);
            var logger = new RunLogger(stdout, fileWriter, args.LogLevel);

            var definitions = new ActionRegistry();
            var executors = new ActionExecutorRegistry();
            BuiltInActions.Register(definitions, executors);

            var options = new ExecutionOptions { ResolvedTargets = resolvedTargets, Log = logger.Message };
            var progress = new InlineProgress<ExecutionProgress>(logger.ActionExecuted);

            logger.RunStart(bot.Name);
            var result = await new BotExecutor(executors).RunAsync(bot, options, progress, ct);
            logger.RunEnd(result);

            return result.Success ? 0 : 1;
        }
        finally
        {
            await DisposeTargetHandlesAsync(resolvedTargets);
        }
```

  And add this helper to the `RunnerApp` class:

```csharp
    private static async Task DisposeTargetHandlesAsync(IReadOnlyDictionary<Guid, ResolvedTarget> targets)
    {
        foreach (var target in targets.Values)
        {
            switch (target.Handle)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }
```

  (Keep the existing `using System;` etc. — `IAsyncDisposable`/`IDisposable` are in `System`.)

- [ ] **Step 4: Build** — `dotnet build ADB.slnx -c Debug --nologo` → `Build succeeded`, 0 errors, 0 warnings.
  Adjust Playwright option-class names to the installed version if the build complains; the
  `IBrowserPage` interface + Tasks 1–3 MUST remain unchanged. If you can't map an op to the installed
  Playwright API, STOP and report BLOCKED.

- [ ] **Step 5: Run the test suite** — `dotnet test ADB.slnx -c Debug --nologo --no-build` → all green
  (no new unit tests; just confirm nothing broke).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Browser/PlaywrightBrowserPage.cs BotRunner/BrowserTargetBinder.cs BotRunner/RunnerApp.cs
git commit -m "feat(browser): Playwright adapter + launch-owned BrowserTargetBinder + run-end disposal"
```

---

## Task 5: Target-picker browser-engine dropdown (WPF, visual)

Adds a browser-engine dropdown for Browser targets in the Test Run target picker. No unit tests.

**Files:**
- Modify: `BotBuilder.Core/Integration/TargetSelectionRow.cs`, `BotBuilder/TargetPickerDialog.xaml`, `BotBuilder/TargetPickerDialog.xaml.cs`

- [ ] **Step 1: Add `IsBrowser` to `TargetSelectionRow`.** Next to `IsWindow`/`IsAndroid`, add:

```csharp
    public bool IsBrowser => Type == BotTargetType.Browser;
```

- [ ] **Step 2: Add a browser-engine dropdown to `TargetPickerDialog.xaml`.** In the row template's column-2
  `StackPanel` (which already holds the Window + Android combos), add a third ComboBox, visible only for
  Browser rows, listing the three engines:

```xml
                            <ComboBox Width="180" Margin="8,0,0,0" VerticalAlignment="Center"
                                      Visibility="{Binding IsBrowser, Converter={StaticResource BoolToVis}}"
                                      Tag="{Binding}" Loaded="OnBrowserComboLoaded" SelectionChanged="OnBrowserChosen" />
```

  (Keep the existing Window + Android combos in the StackPanel exactly as they are.)

- [ ] **Step 3: Add the browser handlers to `TargetPickerDialog.xaml.cs`** (keep all existing members):

```csharp
    private void OnBrowserComboLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            combo.ItemsSource = AdbCore.Browser.BrowserSelector.Engines;
        }
    }

    private void OnBrowserChosen(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string engine, Tag: TargetSelectionRow row })
        {
            row.Selector = $"browser:{engine}";
        }
    }
```

- [ ] **Step 4: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Integration/TargetSelectionRow.cs BotBuilder/TargetPickerDialog.xaml BotBuilder/TargetPickerDialog.xaml.cs
git commit -m "feat(browser): browser-engine dropdown in the Test Run target picker"
```

---

## Task 6: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution** — `dotnet build ADB.slnx -c Debug --nologo` → `0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run the whole test suite** — `dotnet test ADB.slnx -c Debug --nologo --no-build`. New AdbCore
  tests this slice: BrowserSelector 6, Browser nav 4, Browser query 3 = +13. AdbCore.Tests should total 252
  (was 239). Registration counts now 31/28; PaletteViewModelTests 31. Others green.

- [ ] **Step 3: Manual run (user visual verification — requires `playwright install`).**
  - Install browsers once: from a built BotRunner/BotBuilder output dir run `pwsh playwright.ps1 install`
    (Playwright drops a `playwright.ps1` in the build output), or `playwright install`.
  - In BotBuilder add a **Browser** target, build a tiny bot (e.g. **Open URL** `https://example.com` →
    **Get Text** `h1` (resultVar `title`) → **Log** `${title}`), **Run → Test Run**: the picker's Browser
    dropdown offers chromium/firefox/webkit; choosing one sets `browser:<engine>`; **Run** launches the
    browser, navigates, reads the heading, and the log shows the text. The browser closes at run end.
  - With browsers NOT installed, Test Run shows the friendly "have you run `playwright install`… see README"
    message (exit 2), not a crash.

> Hand off to the user for visual confirmation (`playwright install` required) before opening the PR. After
> M7b merges, **M7 is complete** — only M9 (Polish) remains.
