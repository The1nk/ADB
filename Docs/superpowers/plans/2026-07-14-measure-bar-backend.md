# Measure Bar (backend) Implementation Plan — Slice 2a of 5

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Read a solid-fill bar's 0–N value directly from a single captured frame (one pixel scan) instead of template-matching up to 16 images per bar — on both Windows and Android.

**Architecture:** A shared `FrameSourceResolver.Acquire` returns the `FrameSnapshot` to read (the Slice-1 stored frame, or a fresh capture wrapped via `FrameSnapshot.FromBitmap`). A pure `BarMeasureCore` scans the ROI centerline along a configurable direction, classifies each pixel as filled/empty against a Fill and/or Empty color (nearest-color when both set; tolerance when one set — "only empty" means *not* the empty color), and maps the leading filled fraction to an integer value. Two thin actions (`screen.measureBar`, `android.measureBar`) differ only in how they obtain a fresh frame. The **color dropper picker UI is out of scope** here — colors are entered as hex strings for now (Slice 2b adds the eyedropper).

**Tech Stack:** C# / .NET 10, xUnit + hand-rolled fakes, System.Drawing. Builds on Slice 1 (`FrameSnapshot`, `FrameStore`, `FrameSourceConfig`).

**Design doc:** `Docs/superpowers/specs/2026-07-14-fast-frame-reads-and-library-manager-design.md`

**Branch:** `worktree-measure-bar`, stacked on `worktree-frame-store-capture-once` (Slice 1, PR #83). Backend-only; self-mergeable after Slice 1 lands.

**IMPORTANT (Slice 1 lesson):** any task that registers a new action MUST run the FULL suite (`dotnet test ADB.slnx`) — hardcoded action/palette counts in `BuiltInActionsTests` and `PaletteViewModelTests` break otherwise, and filtered runs won't catch it.

---

### Task 1: `FrameSourceResolver` — acquire a snapshot (stored or fresh)

**Files:**
- Create: `AdbCore/Actions/BuiltIn/FrameSourceResolver.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/FrameSourceResolverTests.cs`

- [ ] **Step 1: Write the failing tests** — Create `AdbCore.Tests/Actions/BuiltIn/FrameSourceResolverTests.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class FrameSourceResolverTests
{
    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    [Fact]
    public void Fresh_InvokesCaptureDelegate_AndSnapshotsIt()
    {
        var ctx = new BotExecutionContext();
        var action = new BotAction();
        var calls = 0;

        var snap = FrameSourceResolver.Acquire(Exec(action, ctx), () => { calls++; return new Bitmap(12, 8, PixelFormat.Format32bppArgb); });

        Assert.Equal(1, calls);
        Assert.Equal(12, snap.Width);
        Assert.Equal(8, snap.Height);
    }

    [Fact]
    public void Stored_ReturnsStoredFrame_WithoutCapturing()
    {
        var ctx = new BotExecutionContext();
        using (var bmp = new Bitmap(30, 20, PixelFormat.Format32bppArgb)) { ctx.Frames.Set("f", FrameSnapshot.FromBitmap(bmp)); }
        var action = new BotAction { Config = { [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue, [FrameSourceConfig.FrameNameKey] = "f" } };
        var calls = 0;

        var snap = FrameSourceResolver.Acquire(Exec(action, ctx), () => { calls++; return new Bitmap(1, 1); });

        Assert.Equal(0, calls);
        Assert.Equal(30, snap.Width);
    }

    [Fact]
    public void Stored_Missing_Throws()
    {
        var ctx = new BotExecutionContext();
        var action = new BotAction { Config = { [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue, [FrameSourceConfig.FrameNameKey] = "nope" } };

        Assert.Throws<InvalidOperationException>(() => FrameSourceResolver.Acquire(Exec(action, ctx), () => new Bitmap(1, 1)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test ADB.slnx --filter "FullyQualifiedName~FrameSourceResolverTests"` → FAIL (type missing).

- [ ] **Step 3: Implement** — Create `AdbCore/Actions/BuiltIn/FrameSourceResolver.cs`:

```csharp
using System.Drawing;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Resolves the <see cref="FrameSnapshot"/> a pixel-reading action should read: the named stored
/// frame when Source = Stored (throwing if absent), or a fresh capture wrapped as a snapshot otherwise. The
/// fresh-capture delegate is platform-specific (Win32 HWND capture vs Android screenshot); the caller supplies
/// it, and its returned <see cref="Bitmap"/> is disposed here.</summary>
public static class FrameSourceResolver
{
    public static FrameSnapshot Acquire(ActionExecutionContext context, Func<Bitmap> captureFresh)
    {
        ArgumentNullException.ThrowIfNull(captureFresh);
        if (FrameSourceConfig.UsesStoredFrame(context.Action.Config))
        {
            var name = FrameSourceConfig.FrameNameOf(context.Action.Config);
            if (!context.Context.Frames.TryGet(name, out var snapshot) || snapshot is null)
            {
                throw new InvalidOperationException($"No stored frame named '{name}'. Add a Capture Frame action before this one.");
            }
            return snapshot;
        }

        using var bitmap = captureFresh();
        return FrameSnapshot.FromBitmap(bitmap);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~FrameSourceResolverTests"` → PASS (3). Then `dotnet build ADB.slnx`.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/FrameSourceResolver.cs AdbCore.Tests/Actions/BuiltIn/FrameSourceResolverTests.cs
git commit -m "feat: FrameSourceResolver (acquire stored-or-fresh snapshot)"
```

---

### Task 2: `BarMeasureCore` — scan + classify + measure

**Files:**
- Create: `AdbCore/Actions/BuiltIn/BarMeasureCore.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/BarMeasureCoreTests.cs`

- [ ] **Step 1: Write the failing tests** — Create `AdbCore.Tests/Actions/BuiltIn/BarMeasureCoreTests.cs`:

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

public class BarMeasureCoreTests
{
    // Builds a 20x1 horizontal bar: first `filledPx` columns are `fill`, the rest `empty`.
    private static FrameSnapshot HBar(int width, int filledPx, Color fill, Color empty)
    {
        using var bmp = new Bitmap(width, 1, PixelFormat.Format32bppArgb);
        for (var x = 0; x < width; x++) { bmp.SetPixel(x, 0, x < filledPx ? fill : empty); }
        return FrameSnapshot.FromBitmap(bmp);
    }

    private static Dictionary<string, object> Config(string? fill, string? empty, string dir = "LeftToRight", int min = 0, int max = 15, int? tol = null)
    {
        var c = new Dictionary<string, object>
        {
            [TemplateMatchCore.RegionXKey] = 0,
            [TemplateMatchCore.RegionYKey] = 0,
            [TemplateMatchCore.RegionWidthKey] = 20,
            [TemplateMatchCore.RegionHeightKey] = 1,
            [BarMeasureCore.DirectionKey] = dir,
            [BarMeasureCore.MinValueKey] = min,
            [BarMeasureCore.MaxValueKey] = max,
        };
        if (fill is not null) { c[BarMeasureCore.FillColorKey] = fill; }
        if (empty is not null) { c[BarMeasureCore.EmptyColorKey] = empty; }
        if (tol is int t) { c[BarMeasureCore.ToleranceKey] = t; }
        return c;
    }

    [Fact]
    public void BothColors_HalfFilled_YieldsHalfOfRange()
    {
        var frame = HBar(20, 10, Color.Red, Color.Black);
        var r = BarMeasureCore.Measure(frame, Config("#FF0000", "#000000"));
        Assert.Equal(8, r.Value);           // round(0 + 0.5*15) = round(7.5) = 8 (away-from-zero)
        Assert.Equal(0.5, r.Fraction, 3);
    }

    [Fact]
    public void FillOnly_FullBar_YieldsMax()
    {
        var frame = HBar(20, 20, Color.Lime, Color.Black);
        var r = BarMeasureCore.Measure(frame, Config("#00FF00", null, tol: 40));
        Assert.Equal(15, r.Value);
        Assert.Equal(1.0, r.Fraction, 3);
    }

    [Fact]
    public void EmptyOnly_ClassifiesFillAsNotEmpty()
    {
        // Fill is an arbitrary gradient-ish color; only the empty (black) track is known.
        var frame = HBar(20, 5, Color.FromArgb(10, 200, 30), Color.Black);
        var r = BarMeasureCore.Measure(frame, Config(null, "#000000", tol: 40));
        Assert.Equal(0.25, r.Fraction, 3);  // 5/20
        Assert.Equal(4, r.Value);           // round(0.25*15)=round(3.75)=4
    }

    [Fact]
    public void RightToLeft_MeasuresFromRightEdge()
    {
        // Right 8 columns filled, left 12 empty; RightToLeft sees a leading run of 8.
        using var bmp = new Bitmap(20, 1, PixelFormat.Format32bppArgb);
        for (var x = 0; x < 20; x++) { bmp.SetPixel(x, 0, x >= 12 ? Color.Red : Color.Black); }
        var frame = FrameSnapshot.FromBitmap(bmp);
        var r = BarMeasureCore.Measure(frame, Config("#FF0000", "#000000", dir: "RightToLeft"));
        Assert.Equal(0.4, r.Fraction, 3);   // 8/20
    }

    [Fact]
    public void NoColors_Throws()
    {
        var frame = HBar(20, 10, Color.Red, Color.Black);
        Assert.Throws<ArgumentException>(() => BarMeasureCore.Measure(frame, Config(null, null)));
    }

    [Fact]
    public void NoRegion_Throws()
    {
        var frame = HBar(20, 10, Color.Red, Color.Black);
        var c = new Dictionary<string, object> { [BarMeasureCore.FillColorKey] = "#FF0000" }; // no region
        Assert.Throws<ArgumentException>(() => BarMeasureCore.Measure(frame, c));
    }

    [Fact]
    public void ParseColor_HandlesHashAndBareHex_AndRejectsGarbage()
    {
        Assert.Equal(Color.FromArgb(255, 0, 0).ToArgb(), BarMeasureCore.ParseColor("#FF0000")!.Value.ToArgb());
        Assert.Equal(Color.FromArgb(0, 255, 0).ToArgb(), BarMeasureCore.ParseColor("00FF00")!.Value.ToArgb());
        Assert.Null(BarMeasureCore.ParseColor(""));
        Assert.Null(BarMeasureCore.ParseColor("xyz"));
    }

    [Fact]
    public void Fields_ExposeExpectedKeys()
    {
        var keys = new List<string>();
        foreach (var f in BarMeasureCore.Fields()) { keys.Add(f.Key); }
        Assert.Contains(BarMeasureCore.FillColorKey, keys);
        Assert.Contains(BarMeasureCore.EmptyColorKey, keys);
        Assert.Contains(BarMeasureCore.DirectionKey, keys);
        Assert.Contains(BarMeasureCore.ResultVarKey, keys);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test ADB.slnx --filter "FullyQualifiedName~BarMeasureCoreTests"` → FAIL.

- [ ] **Step 3: Implement** — Create `AdbCore/Actions/BuiltIn/BarMeasureCore.cs`:

```csharp
using System.Drawing;
using System.Globalization;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>The scan direction for a bar: which edge is 0% and which axis to walk.</summary>
public enum BarDirection { LeftToRight, RightToLeft, TopToBottom, BottomToTop }

/// <summary>The result of measuring a bar: the mapped integer value and the raw filled fraction (0..1).</summary>
public readonly record struct BarResult(int Value, double Fraction);

/// <summary>Capture-source-independent core for the Measure Bar actions: reads a solid-fill bar's value from a
/// <see cref="FrameSnapshot"/> by scanning the ROI centerline and mapping the leading filled fraction onto
/// [Min, Max]. Classification: nearest-color when both Fill and Empty are set; within-tolerance of Fill when
/// only Fill is set; NOT-within-tolerance of Empty when only Empty is set.</summary>
public static class BarMeasureCore
{
    public const string FillColorKey = "fillColor";
    public const string EmptyColorKey = "emptyColor";
    public const string ToleranceKey = "tolerance";
    public const string DirectionKey = "direction";
    public const string MinValueKey = "minValue";
    public const string MaxValueKey = "maxValue";
    public const string ResultVarKey = "resultVar";
    public const int DefaultTolerance = 30;
    public const int DefaultMin = 0;
    public const int DefaultMax = 15;
    public const string DefaultResultVar = "bar";

    /// <summary>The Measure-Bar-specific config fields (colors, tolerance, direction, min/max, result var).
    /// The action appends the shared ROI + Source fields around these.</summary>
    public static IEnumerable<ConfigField> Fields() =>
    [
        new ConfigField { Key = FillColorKey, Label = "Fill Color (hex)", Type = ConfigFieldType.String },
        new ConfigField { Key = EmptyColorKey, Label = "Empty Color (hex)", Type = ConfigFieldType.String },
        new ConfigField { Key = ToleranceKey, Label = "Tolerance", Type = ConfigFieldType.Number, DefaultValue = DefaultTolerance },
        new ConfigField
        {
            Key = DirectionKey, Label = "Direction", Type = ConfigFieldType.Enum,
            DefaultValue = nameof(BarDirection.LeftToRight),
            Options = new()
            {
                nameof(BarDirection.LeftToRight), nameof(BarDirection.RightToLeft),
                nameof(BarDirection.TopToBottom), nameof(BarDirection.BottomToTop),
            },
        },
        new ConfigField { Key = MinValueKey, Label = "Min Value", Type = ConfigFieldType.Number, DefaultValue = DefaultMin },
        new ConfigField { Key = MaxValueKey, Label = "Max Value", Type = ConfigFieldType.Number, DefaultValue = DefaultMax },
        new ConfigField { Key = ResultVarKey, Label = "Result Variable", Type = ConfigFieldType.String, DefaultValue = DefaultResultVar },
    ];

    /// <summary>Parses "#RRGGBB" or "RRGGBB" to a <see cref="Color"/>; null for blank/invalid.</summary>
    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) { return null; }
        var s = hex.Trim();
        if (s.StartsWith('#')) { s = s[1..]; }
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) { return null; }
        return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    public static BarResult Measure(FrameSnapshot frame, IReadOnlyDictionary<string, object> config)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var fill = ParseColor(ConfigValues.GetString(config, FillColorKey));
        var empty = ParseColor(ConfigValues.GetString(config, EmptyColorKey));
        if (fill is null && empty is null)
        {
            throw new ArgumentException("Measure Bar requires a Fill Color or an Empty Color (at least one).");
        }

        var roi = TemplateMatchCore.ResolveRegion(config, frame.Width, frame.Height)
            ?? throw new ArgumentException("Measure Bar requires a Region (ROI) with positive width and height.");

        var tolerance = ConfigValues.GetInt(config, ToleranceKey, DefaultTolerance);
        var min = ConfigValues.GetInt(config, MinValueKey, DefaultMin);
        var max = ConfigValues.GetInt(config, MaxValueKey, DefaultMax);
        var direction = ParseDirection(ConfigValues.GetString(config, DirectionKey, nameof(BarDirection.LeftToRight)));

        var (axisLength, run) = ScanRun(frame, roi, direction, fill, empty, tolerance);
        var fraction = axisLength > 0 ? (double)run / axisLength : 0d;
        var value = (int)Math.Round(min + fraction * (max - min), MidpointRounding.AwayFromZero);
        value = Math.Clamp(value, Math.Min(min, max), Math.Max(min, max));
        return new BarResult(value, fraction);
    }

    /// <summary>Writes the value (under <paramref name="prefix"/>) and the fraction (under
    /// <c>&lt;prefix&gt;Fraction</c>) as InvariantCulture strings.</summary>
    public static void WriteResult(IDictionary<string, object> variables, string prefix, BarResult result)
    {
        variables[prefix] = result.Value.ToString(CultureInfo.InvariantCulture);
        variables[$"{prefix}Fraction"] = result.Fraction.ToString(CultureInfo.InvariantCulture);
    }

    private static BarDirection ParseDirection(string value)
        => Enum.TryParse<BarDirection>(value, ignoreCase: true, out var d) ? d : BarDirection.LeftToRight;

    private static (int AxisLength, int Run) ScanRun(FrameSnapshot frame, Rectangle roi, BarDirection dir, Color? fill, Color? empty, int tolerance)
    {
        var run = 0;
        switch (dir)
        {
            case BarDirection.LeftToRight:
            {
                var y = roi.Y + roi.Height / 2;
                for (var x = roi.Left; x < roi.Right; x++) { if (IsFilled(frame.GetPixel(x, y), fill, empty, tolerance)) { run++; } else { break; } }
                return (roi.Width, run);
            }
            case BarDirection.RightToLeft:
            {
                var y = roi.Y + roi.Height / 2;
                for (var x = roi.Right - 1; x >= roi.Left; x--) { if (IsFilled(frame.GetPixel(x, y), fill, empty, tolerance)) { run++; } else { break; } }
                return (roi.Width, run);
            }
            case BarDirection.TopToBottom:
            {
                var x = roi.X + roi.Width / 2;
                for (var y = roi.Top; y < roi.Bottom; y++) { if (IsFilled(frame.GetPixel(x, y), fill, empty, tolerance)) { run++; } else { break; } }
                return (roi.Height, run);
            }
            default: // BottomToTop
            {
                var x = roi.X + roi.Width / 2;
                for (var y = roi.Bottom - 1; y >= roi.Top; y--) { if (IsFilled(frame.GetPixel(x, y), fill, empty, tolerance)) { run++; } else { break; } }
                return (roi.Height, run);
            }
        }
    }

    private static bool IsFilled(Color c, Color? fill, Color? empty, int tolerance)
    {
        if (fill is Color f && empty is Color e) { return Distance(c, f) <= Distance(c, e); }
        if (fill is Color fillOnly) { return Distance(c, fillOnly) <= tolerance; }
        return Distance(c, empty!.Value) > tolerance; // only Empty set: filled = NOT the empty color
    }

    private static int Distance(Color a, Color b)
        => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
}
```

- [ ] **Step 4: Run tests to verify they pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~BarMeasureCoreTests"` → PASS (8). Then `dotnet build ADB.slnx`.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/BarMeasureCore.cs AdbCore.Tests/Actions/BuiltIn/BarMeasureCoreTests.cs
git commit -m "feat: BarMeasureCore (bar scan/classification/measure)"
```

---

### Task 3: `MeasureBarAction` (Windows) + registration

**Files:**
- Create: `AdbCore/Actions/BuiltIn/MeasureBarAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs` (register after `CaptureFrameAction`)
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` (bump counts +1 def/+1 exec)
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs` (bump Screen category +1, total +1)
- Test: `AdbCore.Tests/Actions/BuiltIn/MeasureBarActionTests.cs`

- [ ] **Step 1: Write the failing tests** — Create `AdbCore.Tests/Actions/BuiltIn/MeasureBarActionTests.cs`:

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

public class MeasureBarActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = new FakeWindowHandle(handle) };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    // Capture that returns a 20x1 half-red/half-black bar.
    private sealed class HalfBarCapture : IWindowCapture
    {
        public int Calls { get; private set; }
        public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method)
        {
            Calls++;
            var bmp = new Bitmap(20, 1, PixelFormat.Format32bppArgb);
            for (var x = 0; x < 20; x++) { bmp.SetPixel(x, 0, x < 10 ? Color.Red : Color.Black); }
            return bmp;
        }
    }

    private static BotAction BarAction(Guid id) => new()
    {
        TargetId = id,
        Config =
        {
            [BarMeasureCore.FillColorKey] = "#FF0000",
            [BarMeasureCore.EmptyColorKey] = "#000000",
            [TemplateMatchCore.RegionXKey] = 0,
            [TemplateMatchCore.RegionYKey] = 0,
            [TemplateMatchCore.RegionWidthKey] = 20,
            [TemplateMatchCore.RegionHeightKey] = 1,
            [BarMeasureCore.ResultVarKey] = "hp",
        },
    };

    [Fact]
    public async Task Measure_WritesValueAndFraction_RoutesOut()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var result = await new MeasureBarAction(new HalfBarCapture()).ExecuteAsync(Exec(BarAction(id), ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Equal("8", ctx.Variables["hp"]);          // half of 0..15, away-from-zero
        Assert.True(ctx.Variables.ContainsKey("hpFraction"));
    }

    [Fact]
    public async Task StoredSource_UsesFrame_NotFreshCapture()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        using (var bmp = new Bitmap(20, 1, PixelFormat.Format32bppArgb))
        {
            for (var x = 0; x < 20; x++) { bmp.SetPixel(x, 0, x < 15 ? Color.Red : Color.Black); }
            ctx.Frames.Set("f", FrameSnapshot.FromBitmap(bmp));
        }
        var action = BarAction(id);
        action.Config[FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue;
        action.Config[FrameSourceConfig.FrameNameKey] = "f";
        var capture = new HalfBarCapture();

        var result = await new MeasureBarAction(capture).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal(0, capture.Calls);
        Assert.Equal("11", ctx.Variables["hp"]);          // 15/20 = 0.75 -> round(11.25)=11
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var result = await new MeasureBarAction(new HalfBarCapture()).ExecuteAsync(Exec(BarAction(Guid.NewGuid()), new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new MeasureBarAction(new HalfBarCapture());
        Assert.Equal("screen.measureBar", def.TypeKey);
        Assert.Equal("Measure Bar", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == BarMeasureCore.FillColorKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
        Assert.Contains(def.ConfigFields, f => f.Key == TemplateMatchCore.RegionWidthKey);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test ADB.slnx --filter "FullyQualifiedName~MeasureBarActionTests"` → FAIL.

- [ ] **Step 3: Implement** — Create `AdbCore/Actions/BuiltIn/MeasureBarAction.cs`:

```csharp
using AdbCore.Execution;
using AdbCore.Screen;
using AdbCore.Targets;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Reads a solid-fill bar's value from the target window: captures (or reuses a stored frame), scans
/// the ROI via <see cref="BarMeasureCore"/>, and writes the integer value + fraction to run variables.</summary>
public sealed class MeasureBarAction : IActionDefinition, IActionExecutor
{
    private readonly IWindowCapture _capture;

    public MeasureBarAction(IWindowCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
    }

    public string TypeKey => "screen.measureBar";
    public string DisplayName => "Measure Bar";
    public string Category => "Screen";
    public string Description => "Reads a solid-fill bar's value (0..Max) by scanning its region for the filled run.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } =
    [
        .. BarMeasureCore.Fields(),
        .. TemplateMatchCore.RegionFields(),
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
        var result = BarMeasureCore.Measure(frame, context.Action.Config);

        var prefix = ConfigValues.GetString(context.Action.Config, BarMeasureCore.ResultVarKey, BarMeasureCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = BarMeasureCore.DefaultResultVar; }
        BarMeasureCore.WriteResult(context.Context.Variables, prefix, result);
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
```

- [ ] **Step 4: Register + fix counts.** In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, add after the `CaptureFrameAction` registration:

```csharp
        Add(new MeasureBarAction(windowCapture), definitions, executors);
```

Then update the count-assertion tests (this is a registered action — do NOT skip). In `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs`, read the current asserted definition/executor counts and increase EACH by exactly 1. In `BotBuilder.Core.Tests/PaletteViewModelTests.cs`, read the current asserted **Screen** category count and the **total** count and increase EACH by exactly 1 (Android is unchanged in this task). Do not change any other assertion.

- [ ] **Step 5: Run tests to verify they pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~MeasureBarActionTests"` → PASS (4). Then the FULL suite `dotnet test ADB.slnx` → all pass (this catches count-assertion drift). Then `dotnet build ADB.slnx`.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/MeasureBarAction.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/MeasureBarActionTests.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs
git commit -m "feat: Measure Bar action (Windows) + registration"
```

---

### Task 4: `AndroidMeasureBarAction` + registration

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/AndroidMeasureBarAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs` (register after `AndroidCaptureFrameAction`)
- Modify: `AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs` (bump counts +1 def/+1 exec)
- Modify: `BotBuilder.Core.Tests/PaletteViewModelTests.cs` (bump Android category +1, total +1)
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidMeasureBarActionTests.cs`

- [ ] **Step 1: Write the failing tests** — Create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidMeasureBarActionTests.cs`:

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

public class AndroidMeasureBarActionTests
{
    private static byte[] HalfBarPng()
    {
        using var bmp = new Bitmap(20, 1, PixelFormat.Format32bppArgb);
        for (var x = 0; x < 20; x++) { bmp.SetPixel(x, 0, x < 10 ? Color.Red : Color.Black); }
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

    private static BotAction BarAction(Guid id) => new()
    {
        TargetId = id,
        Config =
        {
            [BarMeasureCore.FillColorKey] = "#FF0000",
            [BarMeasureCore.EmptyColorKey] = "#000000",
            [TemplateMatchCore.RegionXKey] = 0,
            [TemplateMatchCore.RegionYKey] = 0,
            [TemplateMatchCore.RegionWidthKey] = 20,
            [TemplateMatchCore.RegionHeightKey] = 1,
            [BarMeasureCore.ResultVarKey] = "atk",
        },
    };

    [Fact]
    public async Task Measure_WritesValue_RoutesOut()
    {
        var id = Guid.NewGuid();
        var (ctx, dev) = DeviceContext(id, HalfBarPng());
        var result = await new AndroidMeasureBarAction().ExecuteAsync(Exec(BarAction(id), ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Contains("screenshot", dev.Calls);
        Assert.Equal("8", ctx.Variables["atk"]);
    }

    [Fact]
    public async Task NoDevice_Fails()
    {
        var result = await new AndroidMeasureBarAction().ExecuteAsync(Exec(BarAction(Guid.NewGuid()), new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("device", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new AndroidMeasureBarAction();
        Assert.Equal("android.measureBar", def.TypeKey);
        Assert.Equal("Android", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == BarMeasureCore.FillColorKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AndroidMeasureBarActionTests"` → FAIL.

- [ ] **Step 3: Implement** — Create `AdbCore/Actions/BuiltIn/Android/AndroidMeasureBarAction.cs`:

```csharp
using System.Drawing;
using System.IO;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Reads a solid-fill bar's value from the bound device screen: captures (or reuses a stored frame),
/// scans the ROI via <see cref="BarMeasureCore"/>, and writes the integer value + fraction to run variables.</summary>
public sealed class AndroidMeasureBarAction : AndroidActionBase
{
    public override string TypeKey => "android.measureBar";
    public override string DisplayName => "Measure Bar (Android)";
    public override string Description => "Reads a solid-fill bar's value (0..Max) from the device screen by scanning its region.";
    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public override List<ConfigField> ConfigFields { get; } =
    [
        .. BarMeasureCore.Fields(),
        .. TemplateMatchCore.RegionFields(),
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
        var result = BarMeasureCore.Measure(frame, context.Action.Config);

        var prefix = ConfigValues.GetString(context.Action.Config, BarMeasureCore.ResultVarKey, BarMeasureCore.DefaultResultVar);
        if (string.IsNullOrWhiteSpace(prefix)) { prefix = BarMeasureCore.DefaultResultVar; }
        BarMeasureCore.WriteResult(context.Context.Variables, prefix, result);
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
```

- [ ] **Step 4: Register + fix counts.** In `AdbCore/Actions/BuiltIn/BuiltInActions.cs`, add after the `AndroidCaptureFrameAction` registration:

```csharp
        Add(new AndroidMeasureBarAction(), definitions, executors);
```

Then bump the counts again: `BuiltInActionsTests.cs` definition/executor counts +1 each; `PaletteViewModelTests.cs` **Android** category +1 and **total** +1 (Screen unchanged this task). Read the current values first.

- [ ] **Step 5: Run tests to verify they pass** — `dotnet test ADB.slnx --filter "FullyQualifiedName~AndroidMeasureBarActionTests"` → PASS (3). Then FULL suite `dotnet test ADB.slnx` → all pass. Then `dotnet build ADB.slnx`.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/AndroidMeasureBarAction.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/BuiltInActionsTests.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidMeasureBarActionTests.cs
git commit -m "feat: Measure Bar action (Android) + registration"
```

---

### Task 5: Docs (CLAUDE.md, README) + full gate

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Wiki: append to `C:/git/ADB.wiki/Capture-Frame-and-Frame-Source.md` (write only; do NOT commit/push — coordinator handles wiki)

- [ ] **Step 1: Full build + test** — `dotnet build ADB.slnx` (clean) and `dotnet test ADB.slnx` (all pass).

- [ ] **Step 2: CLAUDE.md.** Verify against code first: TypeKeys `screen.measureBar` / `android.measureBar`; config keys `fillColor`, `emptyColor`, `tolerance`, `direction` (LeftToRight/RightToLeft/TopToBottom/BottomToTop), `minValue` (0), `maxValue` (15), `resultVar` (default `bar`); classification (both→nearest-color, fill-only→within tolerance, empty-only→NOT within tolerance of empty). Add a **Key Modules** row:

```
| **Measure Bar** | AdbCore/Actions/BuiltIn/BarMeasureCore.cs | Reads a solid-fill bar's value by scanning the ROI centerline (`screen.measureBar` / `android.measureBar`). Classifies pixels by **Fill** and/or **Empty** hex color (nearest-color when both set; within-`tolerance` of Fill, or NOT-within-`tolerance` of Empty, when one is set), maps the leading filled fraction onto `[minValue, maxValue]` (default 0..15) in the chosen `direction`. Writes the integer to `resultVar` (default `bar`) plus `<resultVar>Fraction`. Reads a fresh capture or a `Stored` frame via `FrameSourceResolver`. |
```

- [ ] **Step 3: README.md.** Add a Measure Bar bullet in "The arsenal" in the goblin voice, facts exact (adapt to surrounding style):

```
- **Measure Bar** — got a health/XP/stat bar? Read its value straight off the pixels in one scan instead of
  matching a pile of images. Tell it the fill (and/or empty) colour, the region, and 0..15 — it hands back the
  number. Windows *and* Android.
```

- [ ] **Step 4: Wiki (write only, no commit/push).** Append a "## Measure Bar" section to `C:/git/ADB.wiki/Capture-Frame-and-Frame-Source.md` documenting the two actions, the Fill/Empty classification (incl. the "only Empty ⇒ NOT the empty colour" rule), direction, min/max, result variables, and that it reads a Fresh or Stored frame. Do NOT run git in `C:/git/ADB.wiki`.

- [ ] **Step 5: Commit the worktree docs**

```bash
git add CLAUDE.md README.md
git commit -m "docs: Measure Bar (CLAUDE.md, README)"
```

---

## Self-Review

**Spec coverage (Slice 2a):** Measure Bar core (scan + Fill/Empty/nearest/NOT-empty classification + direction + min/max) → Task 2. Fresh/Stored source reuse → Task 1 + wired in Tasks 3/4. Windows + Android actions + registration → Tasks 3/4. Docs → Task 5. Color dropper picker UI is explicitly Slice 2b (out of scope).

**Placeholder scan:** none — full code in every code step; count-bump steps say "read current value, +1" because exact numbers depend on merge order, with the full-suite run as the gate.

**Type consistency:** `FrameSourceResolver.Acquire(context, Func<Bitmap>)`; `BarMeasureCore.Measure(FrameSnapshot, config) → BarResult(Value, Fraction)`, `ParseColor`, `WriteResult`, `Fields`, keys `fillColor/emptyColor/tolerance/direction/minValue/maxValue/resultVar`; `BarDirection` enum; actions `screen.measureBar` / `android.measureBar`, single `out` port — consistent across tasks and tests.

## Execution Handoff
Backend-only; self-mergeable after Slice 1 (PR #83) lands. Slice 2b (color dropper picker) is a separate visual slice for the user.
