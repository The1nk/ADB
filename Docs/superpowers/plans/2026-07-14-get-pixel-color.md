# Get Pixel Color Implementation Plan — Slice 3 of 5

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Read a single pixel's color from a captured frame into run variables — Windows + Android — so a bot can branch on an exact color (e.g. detecting a state by a UI dot's color).

**Architecture:** A pure `PixelReadCore` reads a point from a `FrameSnapshot` and writes `<prefix>Hex` / `<prefix>R` / `<prefix>G` / `<prefix>B`. Two thin actions (`screen.getPixelColor`, `android.getPixelColor`) resolve their target, acquire a `FrameSnapshot` via the shared `FrameSourceResolver` (Fresh capture or Stored frame), call `PixelReadCore`, and continue on a single `out` port. Read-only: writes vars, no built-in compare (author branches on the vars).

**Tech Stack:** C# / .NET 10, xUnit + hand-rolled fakes, System.Drawing. Builds on Slice 1 (`FrameSnapshot`, `FrameSourceConfig`) and Slice 2a (`FrameSourceResolver`).

**Design doc:** `Docs/superpowers/specs/2026-07-14-fast-frame-reads-and-library-manager-design.md`

**Branch:** `worktree-get-pixel`, stacked on `worktree-measure-bar` (Slice 2a, PR #84). Backend-only.

**IMPORTANT (recurring lesson):** any task that registers a new action MUST run the FULL suite (`dotnet test ADB.slnx`) — hardcoded action/palette counts in `BuiltInActionsTests` and `PaletteViewModelTests` break otherwise, and filtered runs won't catch it.

---

### Task 1: `PixelReadCore`

**Files:**
- Create: `AdbCore/Actions/BuiltIn/PixelReadCore.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/PixelReadCoreTests.cs`

- [ ] **Step 1: Write the failing tests** — Create `AdbCore.Tests/Actions/BuiltIn/PixelReadCoreTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class PixelReadCoreTests
{
    private static FrameSnapshot TwoByTwo()
    {
        var bmp = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
        bmp.SetPixel(1, 0, Color.FromArgb(255, 40, 50, 60));
        bmp.SetPixel(0, 1, Color.FromArgb(255, 200, 100, 0));
        bmp.SetPixel(1, 1, Color.FromArgb(255, 255, 0, 128));
        using (bmp) { return FrameSnapshot.FromBitmap(bmp); }
    }

    private static Dictionary<string, object> Config(int x, int y, string? prefix = null)
    {
        var c = new Dictionary<string, object> { [PixelReadCore.PointXKey] = x, [PixelReadCore.PointYKey] = y };
        if (prefix is not null) { c[PixelReadCore.ResultVarKey] = prefix; }
        return c;
    }

    [Fact]
    public void ReadInto_WritesHexAndChannels_DefaultPrefix()
    {
        var frame = TwoByTwo();
        var vars = new Dictionary<string, object>();

        PixelReadCore.ReadInto(frame, Config(1, 1), vars);

        Assert.Equal("#FF0080", vars["pixelHex"]);
        Assert.Equal("255", vars["pixelR"]);
        Assert.Equal("0", vars["pixelG"]);
        Assert.Equal("128", vars["pixelB"]);
    }

    [Fact]
    public void ReadInto_CustomPrefix()
    {
        var frame = TwoByTwo();
        var vars = new Dictionary<string, object>();

        PixelReadCore.ReadInto(frame, Config(0, 1, "dot"), vars);

        Assert.Equal("#C86400", vars["dotHex"]); // (200,100,0)
        Assert.Equal("200", vars["dotR"]);
    }

    [Fact]
    public void ReadInto_OutOfRange_Throws()
    {
        var frame = TwoByTwo();
        var vars = new Dictionary<string, object>();
        Assert.Throws<ArgumentException>(() => PixelReadCore.ReadInto(frame, Config(2, 0), vars));
        Assert.Throws<ArgumentException>(() => PixelReadCore.ReadInto(frame, Config(0, -1), vars));
    }

    [Fact]
    public void Fields_ExposeXYAndResultVar()
    {
        var keys = new List<string>();
        foreach (var f in PixelReadCore.Fields()) { keys.Add(f.Key); }
        Assert.Equal(new[] { PixelReadCore.PointXKey, PixelReadCore.PointYKey, PixelReadCore.ResultVarKey }, keys);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test ADB.slnx --filter "FullyQualifiedName~PixelReadCoreTests"` — expect FAIL (type missing).

- [ ] **Step 3: Implement** — Create `AdbCore/Actions/BuiltIn/PixelReadCore.cs`:

```csharp
using System.Globalization;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Capture-source-independent core for the Get Pixel Color actions: reads one pixel from a
/// <see cref="FrameSnapshot"/> and writes its hex (<c>#RRGGBB</c>) and R/G/B channels to run variables under a
/// configurable prefix (default <c>pixel</c>).</summary>
public static class PixelReadCore
{
    public const string PointXKey = "x";
    public const string PointYKey = "y";
    public const string ResultVarKey = "resultVar";
    public const string DefaultResultVar = "pixel";

    /// <summary>The point + result-var fields. The action appends the shared Source (+ capture method) fields.</summary>
    public static IEnumerable<ConfigField> Fields() =>
    [
        new ConfigField { Key = PointXKey, Label = "X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = PointYKey, Label = "Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar },
    ];

    /// <summary>Reads the pixel at (x,y) and writes <c>&lt;prefix&gt;Hex/R/G/B</c> into <paramref name="variables"/>.
    /// Throws <see cref="ArgumentException"/> when the point is outside the frame.</summary>
    public static void ReadInto(FrameSnapshot frame, IReadOnlyDictionary<string, object> config, IDictionary<string, object> variables)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var x = ConfigValues.GetInt(config, PointXKey, 0);
        var y = ConfigValues.GetInt(config, PointYKey, 0);
        if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height)
        {
            throw new ArgumentException($"Get Pixel Color point ({x},{y}) is outside the {frame.Width}x{frame.Height} frame.");
        }

        var prefix = ConfigValues.GetString(config, ResultVarKey, DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = DefaultResultVar; }

        var c = frame.GetPixel(x, y);
        variables[$"{prefix}Hex"] = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        variables[$"{prefix}R"] = c.R.ToString(CultureInfo.InvariantCulture);
        variables[$"{prefix}G"] = c.G.ToString(CultureInfo.InvariantCulture);
        variables[$"{prefix}B"] = c.B.ToString(CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~PixelReadCoreTests"` — expect PASS (4). Then `dotnet build ADB.slnx`.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/PixelReadCore.cs AdbCore.Tests/Actions/BuiltIn/PixelReadCoreTests.cs
git commit -m "feat: PixelReadCore (read a pixel's hex/RGB into variables)"
```

---

### Task 2: `GetPixelColorAction` (Windows) + registration

**Files:**
- Create: `AdbCore/Actions/BuiltIn/GetPixelColorAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs` (register after the `MeasureBarAction` registration)
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` (+1 def, +1 exec)
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs` (+1 Screen, +1 total)
- Test: `AdbCore.Tests/Actions/BuiltIn/GetPixelColorActionTests.cs`

- [ ] **Step 1: Write the failing tests** — Create `AdbCore.Tests/Actions/BuiltIn/GetPixelColorActionTests.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Targets;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class GetPixelColorActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = new FakeWindowHandle(handle) };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private sealed class SolidCapture : IWindowCapture
    {
        public int Calls { get; private set; }
        private readonly Color _color;
        public SolidCapture(Color color) => _color = color;
        public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method)
        {
            Calls++;
            var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(_color);
            return bmp;
        }
    }

    [Fact]
    public async Task Read_WritesColorVars_RoutesOut()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { [PixelReadCore.PointXKey] = 1, [PixelReadCore.PointYKey] = 2, [PixelReadCore.ResultVarKey] = "c" } };

        var result = await new GetPixelColorAction(new SolidCapture(Color.FromArgb(255, 12, 34, 56))).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Equal("#0C2238", ctx.Variables["cHex"]);
        Assert.Equal("12", ctx.Variables["cR"]);
        Assert.Equal("34", ctx.Variables["cG"]);
        Assert.Equal("56", ctx.Variables["cB"]);
    }

    [Fact]
    public async Task StoredSource_UsesFrame_NotFreshCapture()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        using (var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.FromArgb(255, 9, 9, 9)); }
            ctx.Frames.Set("f", FrameSnapshot.FromBitmap(bmp));
        }
        var action = new BotAction { TargetId = id, Config =
        {
            [PixelReadCore.PointXKey] = 0, [PixelReadCore.PointYKey] = 0,
            [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue, [FrameSourceConfig.FrameNameKey] = "f",
        } };
        var capture = new SolidCapture(Color.Red);

        var result = await new GetPixelColorAction(capture).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal(0, capture.Calls);
        Assert.Equal("#090909", ctx.Variables["pixelHex"]);
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var action = new BotAction { Config = { [PixelReadCore.PointXKey] = 0, [PixelReadCore.PointYKey] = 0 } };
        var result = await new GetPixelColorAction(new SolidCapture(Color.Red)).ExecuteAsync(Exec(action, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new GetPixelColorAction(new SolidCapture(Color.Red));
        Assert.Equal("screen.getPixelColor", def.TypeKey);
        Assert.Equal("Get Pixel Color", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == PixelReadCore.PointXKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test ADB.slnx --filter "FullyQualifiedName~GetPixelColorActionTests"` — expect FAIL.

- [ ] **Step 3: Implement** — Create `AdbCore/Actions/BuiltIn/GetPixelColorAction.cs`:

```csharp
using AdbCore.Execution;
using AdbCore.Screen;
using AdbCore.Targets;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Reads a single pixel's color from the target window (fresh capture or a stored frame) into run
/// variables (<c>&lt;prefix&gt;Hex/R/G/B</c>). Read-only; branch on the variables downstream.</summary>
public sealed class GetPixelColorAction : IActionDefinition, IActionExecutor
{
    private readonly IWindowCapture _capture;

    public GetPixelColorAction(IWindowCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
    }

    public string TypeKey => "screen.getPixelColor";
    public string DisplayName => "Get Pixel Color";
    public string Category => "Screen";
    public string Description => "Reads the color of a single pixel into variables (hex + R/G/B).";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } =
    [
        .. PixelReadCore.Fields(),
        .. FrameSourceConfig.Fields(),
        new ConfigField
        {
            Key = ScreenActionBase.CaptureMethodKey, Label = "Capture Method", Type = ConfigFieldType.Enum,
            DefaultValue = nameof(ScreenCaptureMethod.Auto),
            Options = new() { nameof(ScreenCaptureMethod.Auto), nameof(ScreenCaptureMethod.BitBlt) },
        },
    ];
    public bool SupportsRetry => true;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var wh = TargetResolution.ResolveHandle<IWindowHandle>(context);
        if (wh?.GetLiveHandle() is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var method = string.Equals(
            ConfigValues.GetString(context.Action.Config, ScreenActionBase.CaptureMethodKey, nameof(ScreenCaptureMethod.Auto)),
            nameof(ScreenCaptureMethod.BitBlt), StringComparison.OrdinalIgnoreCase)
            ? ScreenCaptureMethod.BitBlt : ScreenCaptureMethod.Auto;

        var frame = FrameSourceResolver.Acquire(context, () => _capture.Capture(hwnd, method));
        PixelReadCore.ReadInto(frame, context.Action.Config, context.Context.Variables);
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
```

- [ ] **Step 4: Register + fix counts.** In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, add immediately AFTER the `Add(new MeasureBarAction(windowCapture), ...)` line:

```csharp
        Add(new GetPixelColorAction(windowCapture), definitions, executors);
```

Then update counts (registers an action — do NOT skip): in `BuiltInActionsTests.cs` READ the current def + exec counts and increase EACH by 1; in `PaletteViewModelTests.cs` READ the **Screen** category count and **total** and increase EACH by 1 (Android unchanged). Update inline comments if present.

- [ ] **Step 5: Run tests to verify they pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~GetPixelColorActionTests"` → PASS (4). Then the **FULL** suite `dotnet test ADB.slnx` → ALL pass. Then `dotnet build ADB.slnx`.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/GetPixelColorAction.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/GetPixelColorActionTests.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs
git commit -m "feat: Get Pixel Color action (Windows) + registration"
```

---

### Task 3: `AndroidGetPixelColorAction` + registration

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/AndroidGetPixelColorAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs` (register after the `AndroidMeasureBarAction` registration)
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` (+1 def, +1 exec)
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs` (+1 Android, +1 total)
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidGetPixelColorActionTests.cs`

- [ ] **Step 1: Write the failing tests** — Create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidGetPixelColorActionTests.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidGetPixelColorActionTests
{
    private static byte[] SolidPng(Color color)
    {
        using var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(color); }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static (BotExecutionContext ctx, FakeAndroidDevice dev) DeviceContext(Guid id, byte[] png)
    {
        var dev = new FakeAndroidDevice { ScreenshotBytes = png };
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:x", Handle = dev };
        return (ctx, dev);
    }

    [Fact]
    public async Task Read_WritesColorVars_RoutesOut()
    {
        var id = Guid.NewGuid();
        var (ctx, dev) = DeviceContext(id, SolidPng(Color.FromArgb(255, 12, 34, 56)));
        var action = new BotAction { TargetId = id, Config = { [PixelReadCore.PointXKey] = 1, [PixelReadCore.PointYKey] = 1, [PixelReadCore.ResultVarKey] = "c" } };

        var result = await new AndroidGetPixelColorAction().ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Contains("screenshot", dev.Calls);
        Assert.Equal("#0C2238", ctx.Variables["cHex"]);
    }

    [Fact]
    public async Task NoDevice_Fails()
    {
        var action = new BotAction { Config = { [PixelReadCore.PointXKey] = 0, [PixelReadCore.PointYKey] = 0 } };
        var result = await new AndroidGetPixelColorAction().ExecuteAsync(Exec(action, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("device", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new AndroidGetPixelColorAction();
        Assert.Equal("android.getPixelColor", def.TypeKey);
        Assert.Equal("Android", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == PixelReadCore.PointXKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AndroidGetPixelColorActionTests"` — expect FAIL.

- [ ] **Step 3: Implement** — Create `AdbCore/Actions/BuiltIn/Android/AndroidGetPixelColorAction.cs`:

```csharp
using System.Drawing;
using System.IO;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Reads a single pixel's color from the device screen (fresh capture or a stored frame) into run
/// variables (<c>&lt;prefix&gt;Hex/R/G/B</c>). Read-only; branch on the variables downstream.</summary>
public sealed class AndroidGetPixelColorAction : AndroidActionBase
{
    public override string TypeKey => "android.getPixelColor";
    public override string DisplayName => "Get Pixel Color (Android)";
    public override string Description => "Reads the color of a single pixel on the device screen into variables (hex + R/G/B).";
    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public override List<ConfigField> ConfigFields { get; } =
    [
        .. PixelReadCore.Fields(),
        .. FrameSourceConfig.Fields(),
    ];
    public override bool SupportsRetry => true;

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var frame = FrameSourceResolver.Acquire(context, () =>
        {
            using var ms = new MemoryStream(device.Screenshot());
            using var decoded = new Bitmap(ms);
            return new Bitmap(decoded); // detached copy so the stream can be disposed
        });
        PixelReadCore.ReadInto(frame, context.Action.Config, context.Context.Variables);
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
```

- [ ] **Step 4: Register + fix counts.** In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, add immediately AFTER the `Add(new AndroidMeasureBarAction(), ...)` line:

```csharp
        Add(new AndroidGetPixelColorAction(), definitions, executors);
```

Then bump counts: `BuiltInActionsTests.cs` def + exec +1 each; `PaletteViewModelTests.cs` **Android** +1 and **total** +1 (Screen unchanged). Read current values first.

- [ ] **Step 5: Run tests to verify they pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AndroidGetPixelColorActionTests"` → PASS (3). Then FULL suite `dotnet test ADB.slnx` → ALL pass. Then `dotnet build ADB.slnx`.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/AndroidGetPixelColorAction.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidGetPixelColorActionTests.cs
git commit -m "feat: Get Pixel Color action (Android) + registration"
```

---

### Task 4: Docs

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Wiki: `C:/git/ADB.wiki/Get-Pixel-Color.md` (+ `_Sidebar.md`) — write only; do NOT commit/push

- [ ] **Step 1: Full build + test.** `dotnet build ADB.slnx` (clean) and `dotnet test ADB.slnx` (all pass).

- [ ] **Step 2: CLAUDE.md.** Verify against code first: TypeKeys `screen.getPixelColor` / `android.getPixelColor`; keys `x`, `y`, `resultVar` (default `pixel`); writes `<prefix>Hex` (`#RRGGBB`, uppercase), `<prefix>R/G/B`; single `out` port; reads Fresh or Stored frame. Add a **Key Modules** row:

```
| **Get Pixel Color** | AdbCore/Actions/BuiltIn/PixelReadCore.cs | Reads one pixel's color into variables (`screen.getPixelColor` / `android.getPixelColor`): `<resultVar>Hex` (`#RRGGBB`), `<resultVar>R/G/B` (default prefix `pixel`) at config point `x`,`y`. Reads a fresh capture or a `Stored` frame via `FrameSourceResolver`. Read-only — branch on the variables. |
```

- [ ] **Step 3: README.md.** Add a Get Pixel Color bullet in "The arsenal" (goblin voice, facts exact), matching the neighbouring bullet style:

```
- **Get Pixel Color** — poke a single pixel and get its colour back (`pixelHex`, `pixelR/G/B`) to branch on.
  Handy for "is that light green yet?" checks. Windows *and* Android.
```

- [ ] **Step 4: Wiki (write only, no commit/push).** Create `C:/git/ADB.wiki/Get-Pixel-Color.md` documenting the two actions/TypeKeys, the `x`/`y`/`resultVar` config, the written variables (`<prefix>Hex` uppercase `#RRGGBB`, `<prefix>R/G/B`), single `out` port, that it reads a Fresh or Stored frame, and the out-of-range failure. Add a `- [Get Pixel Color](Get-Pixel-Color)` link to `_Sidebar.md` if it exists (match its bullet style). Do NOT run git in `C:/git/ADB.wiki`.

- [ ] **Step 5: Commit the worktree docs**

```bash
git add CLAUDE.md README.md
git commit -m "docs: Get Pixel Color (CLAUDE.md, README)"
```

---

## Self-Review

**Spec coverage (Slice 3):** PixelReadCore (read pixel → hex + R/G/B under prefix) → Task 1. Fresh/Stored source via FrameSourceResolver → Tasks 2/3. Windows + Android actions + registration → Tasks 2/3. Read-only, single `out` port, no built-in compare → per the design decision. Docs → Task 4.

**Placeholder scan:** none — full code in every step; count bumps say "read current, +1" with the full-suite gate.

**Type consistency:** `PixelReadCore.ReadInto(FrameSnapshot, config, variables)`, `Fields()`, keys `x`/`y`/`resultVar`, default prefix `pixel`, hex uppercase `#RRGGBB`; actions `screen.getPixelColor` / `android.getPixelColor`, single `out`, ctor injection matching the Measure Bar siblings — consistent across tasks and tests.

## Execution Handoff
Backend-only, stacked on Slice 2a (PR #84) → stacked PR (base `worktree-measure-bar`), retargets to `main` after #84.
