# Frame Store + Capture Frame + Source Selector — Implementation Plan (Slice 1 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-run frame store, a **Capture Frame** action (Windows + Android), and a **Source** selector on the image-matching actions so a batch of reads can reuse one capture instead of re-capturing the full target every time.

**Architecture:** A `FrameSnapshot` holds an immutable 32bpp BGRA pixel buffer (managed `byte[]`, so thread-safe reads and no disposal). A `FrameStore` (concurrent, keyed by name) lives on `BotExecutionContext`. `screen.captureFrame` / `android.captureFrame` grab the target once and store a snapshot. A shared `FrameSourceConfig` helper adds a **Source** (`Fresh`|`Stored`) + **Frame Name** config pair; the Screen and Android image bases consult it and, in Stored mode, match against the snapshot instead of capturing. Default is `Fresh` — existing bots are unchanged.

**Tech Stack:** C# / .NET 10, xUnit with hand-rolled fakes, System.Drawing (LockBits), OpenCvSharp matcher (unchanged), MoonSharp/ADB unaffected.

**Design doc:** `Docs/superpowers/specs/2026-07-14-fast-frame-reads-and-library-manager-design.md`

**This slice is backend-only (AdbCore + AdbCore.Tests). No WPF changes. Safe to build and merge on its own.**

---

### Task 1: `FrameSnapshot` — immutable pixel snapshot

**Files:**
- Create: `AdbCore/Screen/FrameSnapshot.cs`
- Test: `AdbCore.Tests/Screen/FrameSnapshotTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Screen/FrameSnapshotTests.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Screen;

public class FrameSnapshotTests
{
    [Fact]
    public void FromBitmap_RoundTripsPixelColors()
    {
        using var bmp = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
        bmp.SetPixel(1, 0, Color.FromArgb(255, 40, 50, 60));
        bmp.SetPixel(0, 1, Color.FromArgb(255, 70, 80, 90));
        bmp.SetPixel(1, 1, Color.FromArgb(255, 100, 110, 120));

        var snap = FrameSnapshot.FromBitmap(bmp);

        Assert.Equal(2, snap.Width);
        Assert.Equal(2, snap.Height);
        Assert.Equal(Color.FromArgb(255, 10, 20, 30).ToArgb(), snap.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.FromArgb(255, 40, 50, 60).ToArgb(), snap.GetPixel(1, 0).ToArgb());
        Assert.Equal(Color.FromArgb(255, 100, 110, 120).ToArgb(), snap.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void ToBitmap_ReconstructsImage()
    {
        using var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.FromArgb(200, 11, 22, 33));

        var snap = FrameSnapshot.FromBitmap(bmp);
        using var rebuilt = snap.ToBitmap();

        Assert.Equal(bmp.GetPixel(0, 0).ToArgb(), rebuilt.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void GetPixel_OutOfRange_Throws()
    {
        using var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        var snap = FrameSnapshot.FromBitmap(bmp);
        Assert.Throws<ArgumentOutOfRangeException>(() => snap.GetPixel(1, 0));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~FrameSnapshotTests"`
Expected: FAIL — `FrameSnapshot` does not exist (compile error).

- [ ] **Step 3: Implement `FrameSnapshot`**

Create `AdbCore/Screen/FrameSnapshot.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AdbCore.Screen;

/// <summary>An immutable snapshot of a captured frame: a managed 32bpp BGRA pixel buffer with the source's
/// width/height. Being a managed array (not a live GDI <see cref="Bitmap"/>) makes it safe for concurrent
/// reads from parallel branches and frees it from disposal. Convert back to a <see cref="Bitmap"/> only when
/// a consumer (e.g. the template matcher) needs one.</summary>
public sealed class FrameSnapshot
{
    private readonly byte[] _bgra; // Width*Height*4, per pixel: B, G, R, A

    public int Width { get; }
    public int Height { get; }

    private FrameSnapshot(int width, int height, byte[] bgra)
    {
        Width = width;
        Height = height;
        _bgra = bgra;
    }

    /// <summary>Copies <paramref name="bitmap"/> into an immutable 32bpp BGRA snapshot (source format is
    /// normalized to 32bppArgb during the lock, so 24bpp Android PNGs and 32bpp window captures behave alike).</summary>
    public static FrameSnapshot FromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = bitmap.Width * 4;
            var buffer = new byte[rowBytes * bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, buffer, y * rowBytes, rowBytes);
            }
            return new FrameSnapshot(bitmap.Width, bitmap.Height, buffer);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>The color at <paramref name="x"/>,<paramref name="y"/>. Thread-safe (reads a shared byte[]).</summary>
    public Color GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Pixel ({x},{y}) is outside the {Width}x{Height} frame.");
        }
        var i = (y * Width + x) * 4;
        return Color.FromArgb(_bgra[i + 3], _bgra[i + 2], _bgra[i + 1], _bgra[i]); // A,R,G,B from B,G,R,A
    }

    /// <summary>Rebuilds a fresh 32bppArgb <see cref="Bitmap"/> from the snapshot. Caller disposes.</summary>
    public Bitmap ToBitmap()
    {
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = Width * 4;
            for (var y = 0; y < Height; y++)
            {
                Marshal.Copy(_bgra, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~FrameSnapshotTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Screen/FrameSnapshot.cs AdbCore.Tests/Screen/FrameSnapshotTests.cs
git commit -m "feat: FrameSnapshot immutable pixel snapshot for the frame store"
```

---

### Task 2: `FrameStore` on `BotExecutionContext`

**Files:**
- Create: `AdbCore/Execution/FrameStore.cs`
- Modify: `AdbCore/Execution/BotExecutionContext.cs`
- Test: `AdbCore.Tests/Execution/FrameStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Execution/FrameStoreTests.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Execution;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Execution;

public class FrameStoreTests
{
    private static FrameSnapshot Snap(int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        return FrameSnapshot.FromBitmap(bmp);
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsFrame()
    {
        var store = new FrameStore();
        store.Set("hp", Snap(20, 10));

        Assert.True(store.TryGet("hp", out var snap));
        Assert.Equal(20, snap!.Width);
    }

    [Fact]
    public void TryGet_Missing_ReturnsFalse()
    {
        var store = new FrameStore();
        Assert.False(store.TryGet("nope", out var snap));
        Assert.Null(snap);
    }

    [Fact]
    public void Set_Overwrites_SameName()
    {
        var store = new FrameStore();
        store.Set("f", Snap(10, 10));
        store.Set("f", Snap(30, 30));

        Assert.True(store.TryGet("f", out var snap));
        Assert.Equal(30, snap!.Width);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Context_ExposesFrameStore()
    {
        var ctx = new BotExecutionContext();
        Assert.NotNull(ctx.Frames);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~FrameStoreTests"`
Expected: FAIL — `FrameStore` and `BotExecutionContext.Frames` do not exist.

- [ ] **Step 3: Implement `FrameStore` and wire it onto the context**

Create `AdbCore/Execution/FrameStore.cs`:

```csharp
using System.Collections.Concurrent;
using AdbCore.Screen;

namespace AdbCore.Execution;

/// <summary>Per-run store of named <see cref="FrameSnapshot"/>s. A Capture Frame action writes here; readers
/// set to "Stored" source read here. Concurrent so parallel branches can read a shared frame safely.</summary>
public sealed class FrameStore
{
    private readonly ConcurrentDictionary<string, FrameSnapshot> _frames = new();

    public void Set(string name, FrameSnapshot frame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(frame);
        _frames[name] = frame;
    }

    public bool TryGet(string name, out FrameSnapshot? frame) => _frames.TryGetValue(name, out frame);

    public int Count => _frames.Count;
}
```

Modify `AdbCore/Execution/BotExecutionContext.cs` — add the property after `Variables` (line 11):

```csharp
    /// <summary>Named captured frames, written by Capture Frame actions and read by readers set to the
    /// "Stored" source. Runtime-only; never serialized.</summary>
    public FrameStore Frames { get; } = new();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~FrameStoreTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Execution/FrameStore.cs AdbCore/Execution/BotExecutionContext.cs AdbCore.Tests/Execution/FrameStoreTests.cs
git commit -m "feat: FrameStore on BotExecutionContext"
```

---

### Task 3: `FrameSourceConfig` shared helper

**Files:**
- Create: `AdbCore/Actions/BuiltIn/FrameSourceConfig.cs`
- Test: `AdbCore.Tests/Actions/BuiltIn/FrameSourceConfigTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/FrameSourceConfigTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class FrameSourceConfigTests
{
    [Fact]
    public void UsesStoredFrame_DefaultsToFalse_WhenUnset()
    {
        var config = new Dictionary<string, object>();
        Assert.False(FrameSourceConfig.UsesStoredFrame(config));
    }

    [Fact]
    public void UsesStoredFrame_TrueForStored_CaseInsensitive()
    {
        var config = new Dictionary<string, object> { [FrameSourceConfig.SourceKey] = "stored" };
        Assert.True(FrameSourceConfig.UsesStoredFrame(config));
    }

    [Fact]
    public void FrameNameOf_DefaultsToFrame_WhenUnsetOrBlank()
    {
        Assert.Equal("frame", FrameSourceConfig.FrameNameOf(new Dictionary<string, object>()));
        Assert.Equal("frame", FrameSourceConfig.FrameNameOf(new Dictionary<string, object> { [FrameSourceConfig.FrameNameKey] = "  " }));
    }

    [Fact]
    public void FrameNameOf_ReturnsConfiguredName()
    {
        var config = new Dictionary<string, object> { [FrameSourceConfig.FrameNameKey] = "hp" };
        Assert.Equal("hp", FrameSourceConfig.FrameNameOf(config));
    }

    [Fact]
    public void Fields_AreSourceThenFrameName()
    {
        var keys = FrameSourceConfig.Fields().Select(f => f.Key).ToArray();
        Assert.Equal(new[] { FrameSourceConfig.SourceKey, FrameSourceConfig.FrameNameKey }, keys);
        Assert.Equal(ConfigFieldType.Enum, FrameSourceConfig.Fields().First().Type);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~FrameSourceConfigTests"`
Expected: FAIL — `FrameSourceConfig` does not exist.

- [ ] **Step 3: Implement `FrameSourceConfig`**

Create `AdbCore/Actions/BuiltIn/FrameSourceConfig.cs`:

```csharp
namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared "Source" config for readers that can either capture fresh or reuse a stored
/// <see cref="AdbCore.Screen.FrameSnapshot"/>. Default is Fresh, so existing bots are unchanged.</summary>
public static class FrameSourceConfig
{
    public const string SourceKey = "source";
    public const string FreshValue = "Fresh";
    public const string StoredValue = "Stored";
    public const string FrameNameKey = "frameName";
    public const string DefaultFrameName = "frame";

    public static ConfigField SourceField() => new()
    {
        Key = SourceKey,
        Label = "Source",
        Type = ConfigFieldType.Enum,
        DefaultValue = FreshValue,
        Options = new() { FreshValue, StoredValue },
    };

    public static ConfigField FrameNameField() => new()
    {
        Key = FrameNameKey,
        Label = "Frame Name",
        Type = ConfigFieldType.String,
        DefaultValue = DefaultFrameName,
    };

    /// <summary>The Source + Frame Name field pair, in display order.</summary>
    public static IEnumerable<ConfigField> Fields() => [SourceField(), FrameNameField()];

    public static bool UsesStoredFrame(IReadOnlyDictionary<string, object> config)
        => string.Equals(ConfigValues.GetString(config, SourceKey, FreshValue), StoredValue, StringComparison.OrdinalIgnoreCase);

    public static string FrameNameOf(IReadOnlyDictionary<string, object> config)
    {
        var name = ConfigValues.GetString(config, FrameNameKey, DefaultFrameName);
        return string.IsNullOrWhiteSpace(name) ? DefaultFrameName : name;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~FrameSourceConfigTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/FrameSourceConfig.cs AdbCore.Tests/Actions/BuiltIn/FrameSourceConfigTests.cs
git commit -m "feat: FrameSourceConfig shared Source/Frame Name config"
```

---

### Task 4: `CaptureFrameAction` (Windows) + registration

**Files:**
- Create: `AdbCore/Actions/BuiltIn/CaptureFrameAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs:48` (register after `ScreenshotAction`)
- Test: `AdbCore.Tests/Actions/BuiltIn/CaptureFrameActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/CaptureFrameActionTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Tests.Screen;
using AdbCore.Tests.Targets;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class CaptureFrameActionTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = new FakeWindowHandle(handle) };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    [Fact]
    public async Task Capture_StoresNamedFrame()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config = { [FrameSourceConfig.FrameNameKey] = "hp" } };

        var result = await new CaptureFrameAction(new FakeWindowCapture(20, 10)).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.True(ctx.Frames.TryGet("hp", out var snap));
        Assert.Equal(20, snap!.Width);
        Assert.Equal(10, snap.Height);
    }

    [Fact]
    public async Task Capture_DefaultsFrameName_ToFrame()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id };

        await new CaptureFrameAction(new FakeWindowCapture(8, 8)).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(ctx.Frames.TryGet("frame", out _));
    }

    [Fact]
    public async Task NoTarget_Fails()
    {
        var action = new BotAction();
        var result = await new CaptureFrameAction(new FakeWindowCapture(8, 8)).ExecuteAsync(Exec(action, new BotExecutionContext()), default);

        Assert.False(result.Success);
        Assert.Contains("Window", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new CaptureFrameAction(new FakeWindowCapture(8, 8));
        Assert.Equal("screen.captureFrame", def.TypeKey);
        Assert.Equal("Capture Frame", def.DisplayName);
        Assert.Equal("Screen", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.FrameNameKey);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~CaptureFrameActionTests"`
Expected: FAIL — `CaptureFrameAction` does not exist.

- [ ] **Step 3: Implement `CaptureFrameAction`**

Create `AdbCore/Actions/BuiltIn/CaptureFrameAction.cs`:

```csharp
using AdbCore.Execution;
using AdbCore.Screen;
using AdbCore.Targets;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Captures the target window's client area once into a named frame in the run's frame store, so
/// downstream Screen readers set to the "Stored" source can reuse it instead of re-capturing.</summary>
public sealed class CaptureFrameAction : IActionDefinition, IActionExecutor
{
    private readonly IWindowCapture _capture;

    public CaptureFrameAction(IWindowCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
    }

    public string TypeKey => "screen.captureFrame";
    public string DisplayName => "Capture Frame";
    public string Category => "Screen";
    public string Description => "Captures the target window once into a named frame that later Screen readers can reuse.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        FrameSourceConfig.FrameNameField(),
        new ConfigField
        {
            Key = ScreenActionBase.CaptureMethodKey, Label = "Capture Method", Type = ConfigFieldType.Enum,
            DefaultValue = nameof(ScreenCaptureMethod.Auto),
            Options = new() { nameof(ScreenCaptureMethod.Auto), nameof(ScreenCaptureMethod.BitBlt) },
        },
    };
    public bool SupportsRetry => true;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var wh = TargetResolution.ResolveHandle<IWindowHandle>(context);
        if (wh?.GetLiveHandle() is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail("Capture Frame requires a resolved Window target (HWND)."));
        }

        var method = string.Equals(
            ConfigValues.GetString(context.Action.Config, ScreenActionBase.CaptureMethodKey, nameof(ScreenCaptureMethod.Auto)),
            nameof(ScreenCaptureMethod.BitBlt), StringComparison.OrdinalIgnoreCase)
            ? ScreenCaptureMethod.BitBlt : ScreenCaptureMethod.Auto;

        using var shot = _capture.Capture(hwnd, method);
        context.Context.Frames.Set(FrameSourceConfig.FrameNameOf(context.Action.Config), FrameSnapshot.FromBitmap(shot));
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
```

- [ ] **Step 4: Register the action**

Modify `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — add immediately after line 48 (`Add(new ScreenshotAction(windowCapture), ...)`):

```csharp
        Add(new CaptureFrameAction(windowCapture), definitions, executors);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~CaptureFrameActionTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/CaptureFrameAction.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/CaptureFrameActionTests.cs
git commit -m "feat: Capture Frame action (Windows) + registration"
```

---

### Task 5: `AndroidCaptureFrameAction` + registration

**Files:**
- Create: `AdbCore/Actions/BuiltIn/Android/AndroidCaptureFrameAction.cs`
- Modify: `AdbCore/Actions/BuiltIn/BuiltInActions.cs:68` (register after `AndroidScreenshotAction`)
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidCaptureFrameActionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidCaptureFrameActionTests.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidCaptureFrameActionTests
{
    private static byte[] PngBytes(int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
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
    public async Task Capture_StoresDeviceFrame()
    {
        var id = Guid.NewGuid();
        var (ctx, dev) = DeviceContext(id, PngBytes(32, 16));
        var action = new BotAction { TargetId = id, Config = { [FrameSourceConfig.FrameNameKey] = "screen" } };

        var result = await new AndroidCaptureFrameAction().ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal("out", result.OutputPort);
        Assert.Contains("screenshot", dev.Calls);
        Assert.True(ctx.Frames.TryGet("screen", out var snap));
        Assert.Equal(32, snap!.Width);
        Assert.Equal(16, snap.Height);
    }

    [Fact]
    public async Task NoDevice_Fails()
    {
        var action = new BotAction();
        var result = await new AndroidCaptureFrameAction().ExecuteAsync(Exec(action, new BotExecutionContext()), default);
        Assert.False(result.Success);
        Assert.Contains("device", result.ErrorMessage);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new AndroidCaptureFrameAction();
        Assert.Equal("android.captureFrame", def.TypeKey);
        Assert.Equal("Android", def.Category);
        Assert.Equal(new[] { "out" }, def.OutputPorts.Select(p => p.Name));
    }
}
```

Note: `FrameSourceConfig` is in namespace `AdbCore.Actions.BuiltIn`; add `using AdbCore.Actions.BuiltIn;` to the test if the analyzer flags it (the Android test namespace does not auto-import it).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AndroidCaptureFrameActionTests"`
Expected: FAIL — `AndroidCaptureFrameAction` does not exist.

- [ ] **Step 3: Implement `AndroidCaptureFrameAction`**

Create `AdbCore/Actions/BuiltIn/Android/AndroidCaptureFrameAction.cs`:

```csharp
using System.Drawing;
using System.IO;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Captures the bound device's screen once into a named frame in the run's frame store, so
/// downstream readers set to the "Stored" source can reuse it instead of taking another screenshot.</summary>
public sealed class AndroidCaptureFrameAction : AndroidActionBase
{
    public override string TypeKey => "android.captureFrame";
    public override string DisplayName => "Capture Frame (Android)";
    public override string Description => "Captures the device screen once into a named frame that later readers can reuse.";
    public override List<PortDefinition> OutputPorts { get; } = new() { new PortDefinition { Name = "out", Label = "Out" } };
    public override List<ConfigField> ConfigFields { get; } = new() { FrameSourceConfig.FrameNameField() };
    public override bool SupportsRetry => true;

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        using var ms = new MemoryStream(device.Screenshot());
        using var frame = new Bitmap(ms);
        context.Context.Frames.Set(FrameSourceConfig.FrameNameOf(context.Action.Config), FrameSnapshot.FromBitmap(frame));
        return Task.FromResult(ActionResult.Ok("out"));
    }
}
```

- [ ] **Step 4: Register the action**

Modify `AdbCore/Actions/BuiltIn/BuiltInActions.cs` — add immediately after line 68 (`Add(new AndroidScreenshotAction(), ...)`):

```csharp
        Add(new AndroidCaptureFrameAction(), definitions, executors);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AndroidCaptureFrameActionTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/AndroidCaptureFrameAction.cs AdbCore/Actions/BuiltIn/BuiltInActions.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidCaptureFrameActionTests.cs
git commit -m "feat: Capture Frame action (Android) + registration"
```

---

### Task 6: Source selector on the Screen image readers

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/ScreenActionBase.cs` (add `AcquireHaystack`, route `CaptureAndMatch` through it)
- Modify: `AdbCore/Actions/BuiltIn/FindImageAction.cs` (add Source fields)
- Modify: `AdbCore/Actions/BuiltIn/WaitForImageAction.cs` (add Source fields)
- Modify: `AdbCore/Actions/BuiltIn/AssertImageAbsentAction.cs` (add Source fields)
- Test: `AdbCore.Tests/Actions/BuiltIn/StoredFrameSourceTests.cs`

Rationale for per-reader field placement: `ScreenActionBase.ConfigFields` is shared with `ScreenshotAction` (which has no source), so the Source fields go on each *matching* reader's `ActionConfigFields`, not the base.

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/StoredFrameSourceTests.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using AdbCore.Tests.Targets;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class StoredFrameSourceTests
{
    private static BotExecutionContext WindowContext(Guid id, IntPtr handle)
    {
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = new FakeWindowHandle(handle) };
        return ctx;
    }

    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    [Fact]
    public async Task StoredSource_MatchesStoredFrame_WithoutFreshCapture()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        using (var bmp = new Bitmap(50, 40, PixelFormat.Format32bppArgb))
        {
            ctx.Frames.Set("f", FrameSnapshot.FromBitmap(bmp));
        }

        var capture = new FakeWindowCapture(800, 600);
        var matcher = new FakeTemplateMatcher(new MatchResult(1, 2, 3, 4, 0.9));
        var action = new BotAction { TargetId = id, Config =
        {
            [TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(new byte[] { 1 }),
            [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue,
            [FrameSourceConfig.FrameNameKey] = "f",
        } };

        var result = await new FindImageAction(capture, matcher, new FixedRandomSource(0)).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.Equal(0, capture.Calls);              // fresh capture bypassed
        Assert.Equal(50, matcher.LastHaystackWidth); // matched against the stored 50x40 frame
        Assert.Equal(40, matcher.LastHaystackHeight);
    }

    [Fact]
    public async Task FreshSource_StillCaptures_Default()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var capture = new FakeWindowCapture(800, 600);
        var matcher = new FakeTemplateMatcher(new MatchResult(1, 2, 3, 4, 0.9));
        var action = new BotAction { TargetId = id, Config =
        {
            [TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(new byte[] { 1 }),
        } };

        await new FindImageAction(capture, matcher, new FixedRandomSource(0)).ExecuteAsync(Exec(action, ctx), default);

        Assert.Equal(1, capture.Calls);
        Assert.Equal(800, matcher.LastHaystackWidth);
    }

    [Fact]
    public async Task StoredSource_MissingFrame_Throws()
    {
        var id = Guid.NewGuid();
        var ctx = WindowContext(id, (IntPtr)5);
        var action = new BotAction { TargetId = id, Config =
        {
            [TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(new byte[] { 1 }),
            [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue,
            [FrameSourceConfig.FrameNameKey] = "missing",
        } };

        var find = new FindImageAction(new FakeWindowCapture(800, 600), new FakeTemplateMatcher(new MatchResult(0, 0, 1, 1, 1)), new FixedRandomSource(0));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await find.ExecuteAsync(Exec(action, ctx), default));
    }

    [Fact]
    public void FindImage_Definition_IncludesSourceFields()
    {
        var def = new FindImageAction(new FakeWindowCapture(8, 8), new FakeTemplateMatcher(null), new FixedRandomSource(0));
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.FrameNameKey);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~StoredFrameSourceTests"`
Expected: FAIL — `StoredSource_MatchesStoredFrame_WithoutFreshCapture` fails (capture still called; matcher sees 800), and the definition test fails (no Source field yet).

- [ ] **Step 3: Route `CaptureAndMatch` through `AcquireHaystack`**

In `AdbCore/Actions/BuiltIn/ScreenActionBase.cs`, replace the `CaptureAndMatch` method (lines 110-114) with:

```csharp
    protected MatchResult? CaptureAndMatch(ActionExecutionContext context, IntPtr hwnd, ITemplateMatcher matcher, double confidence)
    {
        using var shot = AcquireHaystack(context, hwnd);
        return TemplateMatchCore.MatchInRegion(shot, context.Action.Config, matcher, confidence);
    }

    /// <summary>Returns the haystack to match against: a fresh capture (default), or a stored
    /// <see cref="FrameSnapshot"/> from the run's frame store when Source = Stored. Throws when the named
    /// stored frame is absent (surfaced by the engine as a clear action failure). Caller disposes.</summary>
    private Bitmap AcquireHaystack(ActionExecutionContext context, IntPtr hwnd)
    {
        if (FrameSourceConfig.UsesStoredFrame(context.Action.Config))
        {
            var name = FrameSourceConfig.FrameNameOf(context.Action.Config);
            if (!context.Context.Frames.TryGet(name, out var snapshot) || snapshot is null)
            {
                throw new InvalidOperationException($"No stored frame named '{name}'. Add a Capture Frame action before this one.");
            }
            return snapshot.ToBitmap();
        }
        return _capture.Capture(hwnd, CaptureMethodOf(context));
    }
```

(`FrameSnapshot` lives in `AdbCore.Screen`, already imported by `using AdbCore.Screen;` at the top of the file. `FrameSourceConfig` is in the same `AdbCore.Actions.BuiltIn` namespace.)

- [ ] **Step 4: Add the Source fields to each Screen reader**

In `AdbCore/Actions/BuiltIn/FindImageAction.cs`, change `ActionConfigFields` (lines 33-38) to append the source fields:

```csharp
    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        TemplateNameField(),
        ConfidenceField(),
        ResultVarField(),
        .. FrameSourceConfig.Fields(),
    ];
```

In `AdbCore/Actions/BuiltIn/WaitForImageAction.cs`, find its `ActionConfigFields` collection and append `.. FrameSourceConfig.Fields(),` as the last element (after its existing fields, before the closing `];`).

In `AdbCore/Actions/BuiltIn/AssertImageAbsentAction.cs`, do the same: append `.. FrameSourceConfig.Fields(),` as the last element of its `ActionConfigFields`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~StoredFrameSourceTests"`
Expected: PASS (4 tests).

Then confirm no regression in the existing image tests:

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~FindImageActionTests|FullyQualifiedName~ScreenActionBaseTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Actions/BuiltIn/ScreenActionBase.cs AdbCore/Actions/BuiltIn/FindImageAction.cs AdbCore/Actions/BuiltIn/WaitForImageAction.cs AdbCore/Actions/BuiltIn/AssertImageAbsentAction.cs AdbCore.Tests/Actions/BuiltIn/StoredFrameSourceTests.cs
git commit -m "feat: Source selector on Screen image readers (stored frame reuse)"
```

---

### Task 7: Source selector on the Android image readers

**Files:**
- Modify: `AdbCore/Actions/BuiltIn/Android/AndroidImageActionBase.cs` (add `AcquireHaystack`, route `CaptureAndMatch`; add Source fields to base `ConfigFields`)
- Test: `AdbCore.Tests/Actions/BuiltIn/Android/AndroidStoredFrameSourceTests.cs`

Rationale: `AndroidImageActionBase.ConfigFields` is shared only by the three Android readers (Find/Wait/Assert), so the Source fields go on the base once.

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/BuiltIn/Android/AndroidStoredFrameSourceTests.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidStoredFrameSourceTests
{
    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c) => new(a, c, _ => { });

    private static (BotExecutionContext ctx, FakeAndroidDevice dev) DeviceContext(Guid id)
    {
        var dev = new FakeAndroidDevice();
        var ctx = new BotExecutionContext();
        ctx.Targets[id] = new ResolvedTarget { Type = BotTargetType.AndroidDevice, Selector = "serial:x", Handle = dev };
        return (ctx, dev);
    }

    [Fact]
    public async Task StoredSource_MatchesStoredFrame_WithoutScreenshot()
    {
        var id = Guid.NewGuid();
        var (ctx, dev) = DeviceContext(id);
        using (var bmp = new Bitmap(64, 48, PixelFormat.Format32bppArgb))
        {
            ctx.Frames.Set("f", FrameSnapshot.FromBitmap(bmp));
        }

        var matcher = new FakeTemplateMatcher(new MatchResult(1, 2, 3, 4, 0.9));
        var action = new BotAction { TargetId = id, Config =
        {
            [TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(new byte[] { 1 }),
            [FrameSourceConfig.SourceKey] = FrameSourceConfig.StoredValue,
            [FrameSourceConfig.FrameNameKey] = "f",
        } };

        var result = await new AndroidFindImageAction(matcher, new FixedRandomSource(0)).ExecuteAsync(Exec(action, ctx), default);

        Assert.True(result.Success);
        Assert.DoesNotContain("screenshot", dev.Calls); // device screenshot bypassed
        Assert.Equal(64, matcher.LastHaystackWidth);
    }

    [Fact]
    public void AndroidFindImage_Definition_IncludesSourceFields()
    {
        var def = new AndroidFindImageAction(new FakeTemplateMatcher(null), new FixedRandomSource(0));
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.SourceKey);
        Assert.Contains(def.ConfigFields, f => f.Key == FrameSourceConfig.FrameNameKey);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AndroidStoredFrameSourceTests"`
Expected: FAIL — screenshot still taken; no Source field.

- [ ] **Step 3: Route Android `CaptureAndMatch` through `AcquireHaystack` and add Source fields**

Replace the body of `AdbCore/Actions/BuiltIn/Android/AndroidImageActionBase.cs` with:

```csharp
using System.Drawing;
using System.IO;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Shared base for Android image-matching actions: captures the bound device's framebuffer (or reads
/// a stored frame when Source = Stored), decodes it, and runs template matching via the shared
/// <see cref="TemplateMatchCore"/> so the output contract matches the Screen image actions exactly. Exposes
/// the Source + ROI fields but NO Capture Method field (the framebuffer has no BitBlt/PrintWindow variants).</summary>
public abstract class AndroidImageActionBase : AndroidActionBase
{
    private List<ConfigField>? _configFields;

    public override bool SupportsRetry => true;

    /// <summary>The action's own config fields, shown before the shared Source + ROI fields.</summary>
    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public override List<ConfigField> ConfigFields => _configFields ??=
    [
        .. ActionConfigFields,
        .. FrameSourceConfig.Fields(),
        .. TemplateMatchCore.RegionFields(),
    ];

    /// <summary>Captures the device framebuffer (or reads a stored frame), crops to any ROI, matches the
    /// configured template, and returns the match in full-frame device-pixel coordinates (null if none ≥
    /// confidence).</summary>
    protected static MatchResult? CaptureAndMatch(ActionExecutionContext context, IAndroidDevice device, ITemplateMatcher matcher, double confidence)
    {
        using var frame = AcquireHaystack(context, device);
        return TemplateMatchCore.MatchInRegion(frame, context.Action.Config, matcher, confidence);
    }

    /// <summary>The haystack to match: a fresh device screenshot (default) or a stored
    /// <see cref="FrameSnapshot"/> when Source = Stored. Throws when the named stored frame is absent. Caller
    /// disposes.</summary>
    private static Bitmap AcquireHaystack(ActionExecutionContext context, IAndroidDevice device)
    {
        if (FrameSourceConfig.UsesStoredFrame(context.Action.Config))
        {
            var name = FrameSourceConfig.FrameNameOf(context.Action.Config);
            if (!context.Context.Frames.TryGet(name, out var snapshot) || snapshot is null)
            {
                throw new InvalidOperationException($"No stored frame named '{name}'. Add a Capture Frame action before this one.");
            }
            return snapshot.ToBitmap();
        }

        using var ms = new MemoryStream(device.Screenshot());
        using var decoded = new Bitmap(ms);
        return new Bitmap(decoded); // detached copy so the stream can be disposed safely
    }
}
```

(`FrameSourceConfig` is in the parent `AdbCore.Actions.BuiltIn` namespace, visible from `AdbCore.Actions.BuiltIn.Android` without an extra using; add `using AdbCore.Actions.BuiltIn;` if the compiler flags it.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AndroidStoredFrameSourceTests"`
Expected: PASS (2 tests).

Then confirm no regression across the Android image tests:

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~AndroidFindImage|FullyQualifiedName~AndroidWaitForImage|FullyQualifiedName~AndroidAssertImageAbsent"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AdbCore/Actions/BuiltIn/Android/AndroidImageActionBase.cs AdbCore.Tests/Actions/BuiltIn/Android/AndroidStoredFrameSourceTests.cs
git commit -m "feat: Source selector on Android image readers (stored frame reuse)"
```

---

### Task 8: Full build/test gate + documentation sync

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Create: `../ADB.wiki/Capture-Frame-and-Frame-Source.md` (sibling repo)
- Modify: `../ADB.wiki/_Sidebar.md` (if present — add a link)

- [ ] **Step 1: Full solution build + test**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, 0 errors.

Run: `dotnet test ADB.slnx`
Expected: All tests pass (existing suite + the ~18 new tests from Tasks 1-7).

- [ ] **Step 2: Update `CLAUDE.md`**

In the **Key Modules** table, under the Window Capture / Template Matching area, add a row:

```markdown
| **Frame Store** | AdbCore/Execution/FrameStore.cs (+ AdbCore/Screen/FrameSnapshot.cs) | Per-run named capture cache. **Capture Frame** (`screen.captureFrame` / `android.captureFrame`) grabs the target once into a named `FrameSnapshot` (immutable BGRA buffer); image readers with **Source = Stored** (`FrameSourceConfig`) match against it instead of re-capturing. Runtime-only, never serialized. |
```

In the **`.bot` File Format → Notes** section, add a bullet:

```markdown
- **Frame source.** Find Image / Wait For Image / Assert Image Absent (Screen **and** Android) carry a
  `source` key (`Fresh` default | `Stored`) plus `frameName` (default `frame`). `Stored` reuses a frame
  captured earlier by a **Capture Frame** action (`screen.captureFrame` / `android.captureFrame`), so a batch
  of reads costs one capture. The frame store is runtime-only and never written to the `.bot`.
```

- [ ] **Step 3: Update `README.md`**

In the actions/arsenal section where Screen/Android actions are listed, add Capture Frame in the goblin voice, keeping facts exact. Example addition:

```markdown
- **Capture Frame** — snap the screen *once*, then let a whole gang of Find Image / Measure reads feed off
  that single frame instead of re-grabbing the pixels every time. Works on windows and phones. Set a reader's
  **Source** to *Stored* and point it at your frame's name.
```

- [ ] **Step 4: Update the wiki (sibling repo `../ADB.wiki`)**

Create `../ADB.wiki/Capture-Frame-and-Frame-Source.md`:

```markdown
# Capture Frame & the Frame Source selector

`Capture Frame` grabs the target **once** into a named frame that later image readers can reuse, so a batch
of reads costs a single capture instead of one per read.

## Actions
- **Capture Frame** (`screen.captureFrame`) — captures the Window target's client area.
- **Capture Frame (Android)** (`android.captureFrame`) — captures the device screen.

Both take a **Frame Name** (default `frame`); the Windows variant also takes a **Capture Method**
(`Auto` / `BitBlt`). The frame lives only for the current run and is never saved into the `.bot`.

## Source selector on readers
Find Image, Wait For Image, and Assert Image Absent — on **both** Windows and Android — have a **Source**:
- **Fresh** (default): capture the target now (unchanged behaviour).
- **Stored**: match against the named frame from an earlier Capture Frame. If that frame hasn't been
  captured yet, the action fails with `No stored frame named '<name>'…`.

## Example: read three bars from one capture
1. Capture Frame → Frame Name `iv`.
2. Find Image (Source = Stored, Frame Name `iv`) ×N — all reuse the one capture.
```

If `../ADB.wiki/_Sidebar.md` exists, add a link line under the relevant section:

```markdown
* [Capture Frame & Frame Source](Capture-Frame-and-Frame-Source)
```

- [ ] **Step 5: Commit the main-repo docs**

```bash
git add CLAUDE.md README.md
git commit -m "docs: Capture Frame + Source selector (CLAUDE.md, README)"
```

- [ ] **Step 6: Commit + push the wiki (separate repo)**

```bash
cd ../ADB.wiki
git add Capture-Frame-and-Frame-Source.md _Sidebar.md
git commit -m "Document Capture Frame and the frame Source selector"
git push
cd ../ADB
```

Expected: wiki push succeeds (default branch `master`).

---

## Self-Review

**Spec coverage (Slice 1 scope):**
- Frame store backbone → Tasks 1-2. ✓
- Capture Frame (Windows + Android) → Tasks 4-5. ✓
- Source selector on image families (both platforms) → Tasks 6-7. ✓
- Immutable/thread-safe snapshot for later parallel reads → Task 1 (managed BGRA buffer). ✓
- Default = Capture fresh / no behavior change to existing bots → Tasks 3, 6, 7 (default `Fresh`; existing tests still pass). ✓
- Docs sync (CLAUDE.md + README + wiki) → Task 8. ✓
- Runtime-only, no schema bump → Task 2 (store on context), Task 8 note. ✓

Out-of-slice (later plans): Measure Bar (Slice 2), Get Pixel Color (Slice 3), true-parallel Run Parallel (Slice 4), Library manager (Slice 5). Not covered here by design.

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every command has expected output.

**Type consistency:** `FrameSnapshot.FromBitmap`/`GetPixel`/`ToBitmap`, `FrameStore.Set`/`TryGet`/`Count`, `BotExecutionContext.Frames`, `FrameSourceConfig.SourceKey`/`FreshValue`/`StoredValue`/`FrameNameKey`/`DefaultFrameName`/`SourceField()`/`FrameNameField()`/`Fields()`/`UsesStoredFrame`/`FrameNameOf`, action TypeKeys `screen.captureFrame`/`android.captureFrame`, single `out` port — all consistent across tasks and tests. `AcquireHaystack` is private in both bases with matching throw message.

## Execution Handoff

Backend-only slice — no visual validation needed, so per the repo's "backend-only slices self-merge" rule this can go straight through subagent-driven build → self-review → merge.
