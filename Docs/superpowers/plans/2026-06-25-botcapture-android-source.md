# BotCapture Android Capture Source — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let BotCapture grab a screenshot from a connected ADB device (in addition to Win32 windows), crop it, live-test the match, and save it as a Find-Image template.

**Architecture:** Introduce an `ICaptureSource` abstraction that both a window and an Android device satisfy, and replace the bare `IntPtr` window handle threaded through the picker/confirm/session pipeline with it. The picker gains a `Windows | Android` segmented toggle. ADB devices are listed via the existing `IAdbDevices` and bound via a new `IAndroidDeviceConnector`; the actual frame grab reuses `IAndroidDevice.Screenshot()` — the same framebuffer the runtime `AndroidFindImage` matches against, so saved templates are pixel-correct.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, AdvancedSharpAdbClient (via AdbCore), OpenCvSharp4, xUnit.

**Spec:** `docs/superpowers/specs/2026-06-25-botcapture-android-source-design.md`

**Conventions for every task:** Work in the `C:/git/ADB-android-capture` worktree. Build with `dotnet build ADB.slnx`; test with `dotnet test ADB.slnx`. Narrow a run with `dotnet test ADB.slnx --filter "FullyQualifiedName~<Class>.<Method>"`. NO redirection to `/dev/null` (Windows). C# conventions: file-scoped namespaces, nullable enabled, `_camelCase` private fields, XML doc summaries on public types/methods.

---

## File Structure

**New (`BotCapture.Core`):**
- `ICaptureSource.cs` — the source abstraction.
- `WindowCaptureSource.cs` — window-backed source.
- `AndroidCaptureSource.cs` — device-backed source.
- `SourceKind.cs` — `{ Window, Android }` enum.
- `CaptureSourceRow.cs` — picker row (source + display + thumbnail).
- `SourcePickerViewModel.cs` — replaces `WindowPickerViewModel`.

**New (`AdbCore.Android`):**
- `IAndroidDeviceConnector.cs` — serial → `IAndroidDevice`.
- `AdvancedSharpAdbDeviceConnector.cs` — live connector.

**Modified (`BotCapture.Core`):**
- `SessionRow.cs`, `SessionViewModel.cs`, `PreviewConfirmViewModel.cs` — `IntPtr`+`IWindowCapture` → `ICaptureSource`.
- Delete `WindowPickerViewModel.cs`, `WindowRow.cs` (folded into the new types).

**Modified (`BotCapture`):**
- Rename `Views/WindowPickerView.xaml` + `.xaml.cs` → `Views/SourcePickerView.xaml` + `.xaml.cs`.
- `MainWindow.xaml.cs` — wire the new VM + `ICaptureSource`.

**Modified (`BotCapture.Core.Tests`):**
- `Fakes.cs` (+ Android fakes), `WindowPickerViewModelTests.cs` → `SourcePickerViewModelTests.cs`, `SessionViewModelTests.cs`, `PreviewConfirmViewModelTests.cs`.
- New: `CaptureSourceTests.cs`.

---

## Task 1: `ICaptureSource` + window/Android implementations

**Files:**
- Create: `BotCapture.Core/ICaptureSource.cs`, `BotCapture.Core/WindowCaptureSource.cs`, `BotCapture.Core/AndroidCaptureSource.cs`
- Test: `BotCapture.Core.Tests/CaptureSourceTests.cs`, `BotCapture.Core.Tests/Fakes.cs` (add `FakeAndroidDevice`)

- [ ] **Step 1: Add `FakeAndroidDevice` to `Fakes.cs`**

Append to `BotCapture.Core.Tests/Fakes.cs`:

```csharp
internal sealed class FakeAndroidDevice : AdbCore.Android.IAndroidDevice
{
    /// <summary>PNG bytes returned by Screenshot(); defaults to a 6x4 image. Set Throw to simulate a dead device.</summary>
    public byte[]? Png;
    public Exception? Throw;
    public int ScreenshotCalls;

    public byte[] Screenshot()
    {
        ScreenshotCalls++;
        if (Throw is not null) throw Throw;
        if (Png is not null) return Png;
        using var bmp = new System.Drawing.Bitmap(6, 4);
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    public void Tap(int x, int y) { }
    public void Swipe(int x1, int y1, int x2, int y2, int durationMs) { }
    public void PressBack() { }
    public void LaunchApp(string package) { }
    public void InstallApk(string apkPath) { }
}
```

- [ ] **Step 2: Write the failing tests**

Create `BotCapture.Core.Tests/CaptureSourceTests.cs`:

```csharp
using AdbCore.Screen;
using AdbCore.Targets;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class CaptureSourceTests
{
    [Fact]
    public void Window_Capture_UsesHandleAndAutoMethod_AndExposesInfo()
    {
        var capture = new FakeWindowCapture();
        var src = new WindowCaptureSource(new WindowInfo((IntPtr)42, "Game", "game.exe"), capture);

        using var bmp = src.Capture();

        Assert.Equal("Game", src.Label);
        Assert.Equal("game.exe", src.SubLabel);
        Assert.Equal((IntPtr)42, capture.Calls[^1].Handle);
        Assert.Equal(ScreenCaptureMethod.Auto, capture.Calls[^1].Method);
        Assert.NotNull(bmp);
    }

    [Fact]
    public void Android_Capture_DecodesScreenshotPng_AndExposesSerialAndState()
    {
        var device = new FakeAndroidDevice(); // default 6x4 PNG
        var src = new AndroidCaptureSource("emulator-5554", "device", device);

        using var bmp = src.Capture();

        Assert.Equal("emulator-5554", src.Label);
        Assert.Equal("device", src.SubLabel);
        Assert.Equal(6, bmp.Width);
        Assert.Equal(4, bmp.Height);
        Assert.Equal(1, device.ScreenshotCalls);
    }
}
```

- [ ] **Step 3: Run tests, verify they fail to compile/run**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~CaptureSourceTests"`
Expected: FAIL (types `ICaptureSource`, `WindowCaptureSource`, `AndroidCaptureSource` do not exist).

- [ ] **Step 4: Create `ICaptureSource.cs`**

```csharp
using System.Drawing;

namespace BotCapture.Core;

/// <summary>A re-capturable screenshot source — a Win32 window or a connected Android device. Lets the
/// picker, live Test Match, and standalone Retest grab a fresh frame without knowing which kind it is.</summary>
public interface ICaptureSource
{
    /// <summary>Primary display name (window title / device serial).</summary>
    string Label { get; }

    /// <summary>Secondary display line (process name / device state).</summary>
    string SubLabel { get; }

    /// <summary>Grabs a fresh frame. The caller owns and disposes the result.</summary>
    Bitmap Capture();
}
```

- [ ] **Step 5: Create `WindowCaptureSource.cs`**

```csharp
using System.Drawing;
using AdbCore.Screen;
using AdbCore.Targets;

namespace BotCapture.Core;

/// <summary>An <see cref="ICaptureSource"/> backed by a Win32 window handle.</summary>
public sealed class WindowCaptureSource : ICaptureSource
{
    private readonly WindowInfo _info;
    private readonly IWindowCapture _capture;

    public WindowCaptureSource(WindowInfo info, IWindowCapture capture)
    {
        _info = info;
        _capture = capture;
    }

    public string Label => _info.Title;
    public string SubLabel => _info.ProcessName;

    public Bitmap Capture() => _capture.Capture(_info.Handle, ScreenCaptureMethod.Auto);
}
```

- [ ] **Step 6: Create `AndroidCaptureSource.cs`**

```csharp
using System.Drawing;
using System.IO;
using AdbCore.Android;

namespace BotCapture.Core;

/// <summary>An <see cref="ICaptureSource"/> backed by a connected Android device. Frames come from the ADB
/// framebuffer (<see cref="IAndroidDevice.Screenshot"/>) — the same path the runtime Android Find Image
/// action matches against, so captured templates are device-pixel correct.</summary>
public sealed class AndroidCaptureSource : ICaptureSource
{
    private readonly IAndroidDevice _device;

    public AndroidCaptureSource(string serial, string state, IAndroidDevice device)
    {
        Label = serial;
        SubLabel = state;
        _device = device;
    }

    public string Label { get; }
    public string SubLabel { get; }

    public Bitmap Capture()
    {
        using var ms = new MemoryStream(_device.Screenshot());
        using var decoded = new Bitmap(ms);
        return new Bitmap(decoded); // detached copy so the MemoryStream can be disposed safely
    }
}
```

- [ ] **Step 7: Run tests, verify pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~CaptureSourceTests"`
Expected: PASS (2 tests).

- [ ] **Step 8: Commit**

```bash
git add BotCapture.Core/ICaptureSource.cs BotCapture.Core/WindowCaptureSource.cs BotCapture.Core/AndroidCaptureSource.cs BotCapture.Core.Tests/CaptureSourceTests.cs BotCapture.Core.Tests/Fakes.cs
git commit -m "Add ICaptureSource with window + Android implementations"
```

---

## Task 2: `IAndroidDeviceConnector` (interface + live impl)

**Files:**
- Create: `AdbCore/Android/IAndroidDeviceConnector.cs`, `AdbCore/Android/AdvancedSharpAdbDeviceConnector.cs`

No unit test: the live connector needs a real ADB server/device (mirrors the existing `AdvancedSharpAdbDevices`, which is verified by hand). The interface is what the picker VM tests fake.

- [ ] **Step 1: Create `IAndroidDeviceConnector.cs`**

```csharp
namespace AdbCore.Android;

/// <summary>Binds to a connected ADB device by serial, returning an <see cref="IAndroidDevice"/> for
/// capture/automation. Lets callers (e.g. the BotCapture source picker) build a device handle without
/// depending on AdvancedSharpAdbClient directly.</summary>
public interface IAndroidDeviceConnector
{
    /// <summary>Binds the device with the given serial. Throws if it is not currently connected.</summary>
    IAndroidDevice Connect(string serial);
}
```

- [ ] **Step 2: Create `AdvancedSharpAdbDeviceConnector.cs`**

```csharp
using AdvancedSharpAdbClient;

namespace AdbCore.Android;

/// <summary>Live <see cref="IAndroidDeviceConnector"/>: starts the ADB server (locating adb via PATH),
/// resolves the device by serial, and wraps it in an <see cref="AdvancedSharpAdbDevice"/>. Verified live —
/// needs a real device.</summary>
public sealed class AdvancedSharpAdbDeviceConnector : IAndroidDeviceConnector
{
    public IAndroidDevice Connect(string serial)
    {
        AdbServer.Instance.StartServer(adbPath: "adb", restartServerIfNewer: false);
        var client = new AdbClient();
        var device = client.GetDevices().FirstOrDefault(d => d.Serial == serial)
            ?? throw new InvalidOperationException($"ADB device '{serial}' is not currently connected.");
        return new AdvancedSharpAdbDevice(client, device);
    }
}
```

- [ ] **Step 3: Build, verify it compiles**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add AdbCore/Android/IAndroidDeviceConnector.cs AdbCore/Android/AdvancedSharpAdbDeviceConnector.cs
git commit -m "Add IAndroidDeviceConnector for binding a device by serial"
```

---

## Task 3: Refactor `SessionRow` to hold an `ICaptureSource`

**Files:**
- Modify: `BotCapture.Core/SessionRow.cs`

- [ ] **Step 1: Replace `SourceHandle` with `Source`**

Replace the body of `BotCapture.Core/SessionRow.cs` with:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>A capture saved during the current session: its file, the confidence it was saved with, the
/// source it came from (for re-testing), and the last re-test result (null = not yet tested).</summary>
public partial class SessionRow : ObservableObject
{
    private double _confidence;

    public SessionRow(string filePath, double confidence, ICaptureSource source)
    {
        FilePath = filePath;
        _confidence = confidence;
        Source = source;
    }

    public string FilePath { get; }
    public ICaptureSource Source { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>The saved confidence; updatable when the template is re-edited.</summary>
    public double Confidence
    {
        get => _confidence;
        set => SetProperty(ref _confidence, value);
    }

    /// <summary>Last re-test result: null = untested, true = matched at <see cref="Confidence"/>, false = not.</summary>
    [ObservableProperty] private bool? _lastRetestMatched;
}
```

- [ ] **Step 2: Build (expect downstream errors — fixed in Tasks 4–5/8)**

Run: `dotnet build BotCapture.Core/BotCapture.Core.csproj`
Expected: errors only in `SessionViewModel` / `PreviewConfirmViewModel` (next tasks). `SessionRow` itself compiles. If `SessionRow` shows errors, fix before moving on.

- [ ] **Step 3: Commit**

```bash
git add BotCapture.Core/SessionRow.cs
git commit -m "SessionRow: carry an ICaptureSource instead of a window handle"
```

---

## Task 4: Refactor `SessionViewModel` (Add + Retest via source)

**Files:**
- Modify: `BotCapture.Core/SessionViewModel.cs`
- Test: `BotCapture.Core.Tests/SessionViewModelTests.cs`

- [ ] **Step 1: Rewrite the tests to the `ICaptureSource` shape**

Add to `BotCapture.Core.Tests/Fakes.cs`:

```csharp
internal sealed class FakeCaptureSource : ICaptureSource
{
    public string Label { get; set; } = "fake";
    public string SubLabel { get; set; } = "fake";
    public Func<System.Drawing.Bitmap>? Behavior;
    public int CaptureCalls;

    public System.Drawing.Bitmap Capture()
    {
        CaptureCalls++;
        return Behavior is not null ? Behavior() : new System.Drawing.Bitmap(8, 8);
    }
}
```

Replace `BotCapture.Core.Tests/SessionViewModelTests.cs` with:

```csharp
using AdbCore.Screen;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class SessionViewModelTests
{
    private static SessionViewModel Make(FakeTemplateMatcher matcher) =>
        new(matcher, saveFolder: @"C:\bots");

    [Fact]
    public void Add_AppendsRowWithDetails()
    {
        var vm = Make(new FakeTemplateMatcher());
        var source = new FakeCaptureSource();

        var row = vm.Add(@"C:\bots\a.png", 0.88, source);

        Assert.Single(vm.Rows);
        Assert.Same(row, vm.Rows[0]);
        Assert.Equal(@"C:\bots\a.png", row.FilePath);
        Assert.Equal("a.png", row.FileName);
        Assert.Equal(0.88, row.Confidence, 3);
        Assert.Same(source, row.Source);
        Assert.Null(row.LastRetestMatched);
    }

    [Fact]
    public void Remove_DropsRow()
    {
        var vm = Make(new FakeTemplateMatcher());
        var row = vm.Add(@"C:\bots\a.png", 0.9, new FakeCaptureSource());

        vm.Remove(row);

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Retest_Match_SetsGreen_RecapturesSource_UsesRowConfidenceAndTemplate()
    {
        var matcher = new FakeTemplateMatcher { Next = new MatchResult(0, 0, 4, 4, 0.97) };
        var vm = Make(matcher);
        var source = new FakeCaptureSource();
        var row = vm.Add(@"C:\bots\a.png", 0.80, source);

        vm.Retest(row);

        Assert.True(row.LastRetestMatched);
        Assert.Equal(1, source.CaptureCalls);
        Assert.Equal(0.80, matcher.LastMinConfidence, 3);
        Assert.Equal(@"C:\bots\a.png", matcher.LastTemplatePath);
    }

    [Fact]
    public void Retest_NoMatch_SetsRed()
    {
        var vm = Make(new FakeTemplateMatcher { Next = null });
        var row = vm.Add(@"C:\bots\a.png", 0.95, new FakeCaptureSource());

        vm.Retest(row);

        Assert.False(row.LastRetestMatched);
    }

    [Fact]
    public void Retest_CaptureThrows_SetsRed_NoException()
    {
        var vm = Make(new FakeTemplateMatcher());
        var source = new FakeCaptureSource { Behavior = () => throw new InvalidOperationException("device gone") };
        var row = vm.Add(@"C:\bots\a.png", 0.9, source);

        vm.Retest(row);

        Assert.False(row.LastRetestMatched);
    }
}
```

- [ ] **Step 2: Run tests, verify failure**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~SessionViewModelTests"`
Expected: FAIL to compile (`SessionViewModel` ctor still takes `IWindowCapture`; `Add` still takes `IntPtr`).

- [ ] **Step 3: Rewrite `SessionViewModel.cs`**

```csharp
using System.Collections.ObjectModel;
using AdbCore.Screen;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Standalone session state: the captures saved so far, the save folder, and re-testing a saved
/// template against a fresh capture of the source it came from.</summary>
public partial class SessionViewModel : ObservableObject
{
    private readonly ITemplateMatcher _matcher;
    private string _saveFolder;

    public SessionViewModel(ITemplateMatcher matcher, string saveFolder)
    {
        _matcher = matcher;
        _saveFolder = saveFolder;
    }

    public ObservableCollection<SessionRow> Rows { get; } = new();

    /// <summary>The folder new captures are saved into (changeable via the panel's Browse button).</summary>
    public string SaveFolder
    {
        get => _saveFolder;
        set => SetProperty(ref _saveFolder, value);
    }

    /// <summary>Appends a saved capture as a session row and returns it.</summary>
    public SessionRow Add(string filePath, double confidence, ICaptureSource source)
    {
        var row = new SessionRow(filePath, confidence, source);
        Rows.Add(row);
        return row;
    }

    public void Remove(SessionRow row) => Rows.Remove(row);

    /// <summary>Re-captures the row's source and matches its saved template at the row's confidence,
    /// updating <see cref="SessionRow.LastRetestMatched"/> (true = matched). Never throws.</summary>
    public void Retest(SessionRow row)
    {
        try
        {
            using var fresh = row.Source.Capture();
            row.LastRetestMatched = _matcher.Match(fresh, row.FilePath, row.Confidence) is not null;
        }
        catch
        {
            row.LastRetestMatched = false; // missing/unreadable template or capture failure -> red
        }
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~SessionViewModelTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add BotCapture.Core/SessionViewModel.cs BotCapture.Core.Tests/SessionViewModelTests.cs BotCapture.Core.Tests/Fakes.cs
git commit -m "SessionViewModel: retest via ICaptureSource, drop window-capture dependency"
```

---

## Task 5: Refactor `PreviewConfirmViewModel` (Test Match via source)

**Files:**
- Modify: `BotCapture.Core/PreviewConfirmViewModel.cs`
- Test: `BotCapture.Core.Tests/PreviewConfirmViewModelTests.cs`

- [ ] **Step 1: Update the test helper to inject an `ICaptureSource`**

In `BotCapture.Core.Tests/PreviewConfirmViewModelTests.cs`, replace the `Make` helper and remove the now-unused `FakeWindowCapture` parameter:

```csharp
    private static PreviewConfirmViewModel Make(
        string dir, FakeTemplateMatcher matcher, out Bitmap crop)
    {
        crop = new Bitmap(12, 8);
        return new PreviewConfirmViewModel(crop, new FakeCaptureSource(), matcher, new CaptureSaver(dir));
    }
```

Then update each call site in that file from `Make(dir, new FakeWindowCapture(), matcher, out var crop)` to `Make(dir, matcher, out var crop)` (and the two that passed `new FakeTemplateMatcher()` likewise drop the `FakeWindowCapture` arg). The `TestMatch_ScoreAtOrAboveConfidence_IsMatched` assertion `Assert.Equal(-1.0, matcher.LastMinConfidence, 3)` stays — the best-match floor is unchanged.

- [ ] **Step 2: Run tests, verify failure**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~PreviewConfirmViewModelTests"`
Expected: FAIL to compile (ctor still takes `IntPtr` + `IWindowCapture`).

- [ ] **Step 3: Rewrite `PreviewConfirmViewModel.cs`**

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Screen;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Drives the preview/confirm step: holds the cropped template, the chosen filename and
/// confidence, runs a live Test Match against a fresh capture of the source, and saves the template
/// (PNG + confidence sidecar). Owns <see cref="Crop"/>.</summary>
public partial class PreviewConfirmViewModel : ObservableObject, IDisposable
{
    // Template-match score is CCOEFF_NORMED in [-1, 1]; asking for -1.0 always returns the best match,
    // so the score is shown even when it's below the user's threshold.
    private const double BestMatchFloor = -1.0;

    private readonly ICaptureSource _source;
    private readonly ITemplateMatcher _matcher;
    private readonly CaptureSaver _saver;

    public PreviewConfirmViewModel(
        Bitmap crop, ICaptureSource source, ITemplateMatcher matcher, CaptureSaver saver)
    {
        Crop = crop;
        _source = source;
        _matcher = matcher;
        _saver = saver;
        _fileName = saver.NextFileName();
    }

    /// <summary>The cropped template image to be saved.</summary>
    public Bitmap Crop { get; }

    [ObservableProperty] private TestMatchOutcome? _lastOutcome;

    private string _fileName;
    private double _confidence = 0.9;

    /// <summary>The chosen output filename (defaults to the saver's next free name).</summary>
    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    /// <summary>Match threshold in [0, 1] (default 0.9). Out-of-range assignments clamp.</summary>
    public double Confidence
    {
        get => _confidence;
        set => SetProperty(ref _confidence, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>Re-captures the source and matches the crop against it, recording the best score and
    /// whether it met <see cref="Confidence"/> into <see cref="LastOutcome"/>. Never throws.</summary>
    public void TestMatch()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"botcap_test_{Guid.NewGuid():N}.png");
        try
        {
            Crop.Save(tempPath, ImageFormat.Png);
            using var fresh = _source.Capture();
            var best = _matcher.Match(fresh, tempPath, BestMatchFloor);
            LastOutcome = best is MatchResult m
                ? new TestMatchOutcome(m.Score >= Confidence, m.Score, m, Error: null)
                : new TestMatchOutcome(Matched: false, Score: null, Location: null, Error: "No match could be computed.");
        }
        catch (Exception ex)
        {
            LastOutcome = new TestMatchOutcome(Matched: false, Score: null, Location: null, Error: ex.Message);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* temp cleanup is best-effort */ }
        }
    }

    /// <summary>Writes the template (PNG + confidence sidecar) under the chosen filename.</summary>
    public void Save() => _saver.Save(Crop, FileName, Confidence);

    public void Dispose() => Crop.Dispose();
}
```

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~PreviewConfirmViewModelTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add BotCapture.Core/PreviewConfirmViewModel.cs BotCapture.Core.Tests/PreviewConfirmViewModelTests.cs
git commit -m "PreviewConfirmViewModel: Test Match re-captures via ICaptureSource"
```

---

## Task 6: `SourcePickerViewModel` (windows + Android, toggle, availability)

**Files:**
- Create: `BotCapture.Core/SourceKind.cs`, `BotCapture.Core/CaptureSourceRow.cs`, `BotCapture.Core/SourcePickerViewModel.cs`
- Delete: `BotCapture.Core/WindowPickerViewModel.cs`, `BotCapture.Core/WindowRow.cs`
- Test: rename `BotCapture.Core.Tests/WindowPickerViewModelTests.cs` → `SourcePickerViewModelTests.cs`; add `FakeAdbDevices` + `FakeAndroidDeviceConnector` to `Fakes.cs`

- [ ] **Step 1: Add Android fakes to `Fakes.cs`**

```csharp
internal sealed class FakeAdbDevices : AdbCore.Android.IAdbDevices
{
    public IReadOnlyList<AdbCore.Android.AdbDeviceInfo> Result = Array.Empty<AdbCore.Android.AdbDeviceInfo>();
    public Exception? Throw;
    public IReadOnlyList<AdbCore.Android.AdbDeviceInfo> List()
        => Throw is not null ? throw Throw : Result;
}

internal sealed class FakeAndroidDeviceConnector : AdbCore.Android.IAndroidDeviceConnector
{
    public Func<string, AdbCore.Android.IAndroidDevice>? Behavior;
    public AdbCore.Android.IAndroidDevice Connect(string serial)
        => Behavior is not null ? Behavior(serial) : new FakeAndroidDevice();
}
```

- [ ] **Step 2: Write the failing tests**

Delete `WindowPickerViewModelTests.cs` and create `BotCapture.Core.Tests/SourcePickerViewModelTests.cs`:

```csharp
using AdbCore.Android;
using AdbCore.Screen;
using AdbCore.Targets;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class SourcePickerViewModelTests
{
    private static SourcePickerViewModel Make(
        out FakeWindowEnumerator windows, out FakeWindowCapture capture,
        out FakeAdbDevices devices, out FakeAndroidDeviceConnector connector)
    {
        windows = new FakeWindowEnumerator();
        capture = new FakeWindowCapture();
        devices = new FakeAdbDevices();
        connector = new FakeAndroidDeviceConnector();
        return new SourcePickerViewModel(windows, capture, devices, connector);
    }

    [Fact]
    public void Refresh_Windows_MapsEnumeratedWindowsToRowsInOrder()
    {
        var vm = Make(out var windows, out _, out _, out _);
        windows.Result = new[]
        {
            new WindowInfo((IntPtr)1, "Alpha", "alpha"),
            new WindowInfo((IntPtr)2, "Beta", "beta"),
        };

        vm.Refresh();

        Assert.Equal(2, vm.Sources.Count);
        Assert.Equal("Alpha", vm.Sources[0].Label);
        Assert.Equal("beta", vm.Sources[1].SubLabel);
        Assert.NotNull(vm.Sources[0].ThumbnailPng);
    }

    [Fact]
    public void SwitchingKindToAndroid_RebuildsListFromDevices()
    {
        var vm = Make(out var windows, out _, out var devices, out _);
        windows.Result = new[] { new WindowInfo((IntPtr)1, "Alpha", "alpha") };
        devices.Result = new[] { new AdbDeviceInfo("emulator-5554", "device") };
        vm.Refresh(); // windows kind (default)
        Assert.Single(vm.Sources);

        vm.Kind = SourceKind.Android;

        Assert.Single(vm.Sources);
        Assert.Equal("emulator-5554", vm.Sources[0].Label);
        Assert.Equal("device", vm.Sources[0].SubLabel);
        Assert.Null(vm.UnavailableReason);
    }

    [Fact]
    public void Android_AdbMissing_SetsUnavailableReason_NoRows()
    {
        var vm = Make(out _, out _, out var devices, out _);
        devices.Throw = new InvalidOperationException("adb server failed to start");

        vm.Kind = SourceKind.Android;

        Assert.Empty(vm.Sources);
        Assert.False(string.IsNullOrEmpty(vm.UnavailableReason));
        Assert.Contains("adb", vm.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Android_NoDevices_SetsUnavailableReason_NoRows()
    {
        var vm = Make(out _, out _, out var devices, out _);
        devices.Result = Array.Empty<AdbDeviceInfo>();

        vm.Kind = SourceKind.Android;

        Assert.Empty(vm.Sources);
        Assert.Contains("No devices", vm.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CaptureSelected_UsesSelectedSource_SetsCapturedImage()
    {
        var vm = Make(out _, out _, out _, out _);
        var source = new FakeCaptureSource();
        vm.SelectedSource = new CaptureSourceRow(source, "X", "x", null);

        var ok = vm.CaptureSelected();

        Assert.True(ok);
        Assert.NotNull(vm.CapturedImage);
        Assert.Equal(1, source.CaptureCalls);
        Assert.Same(source, vm.SelectedCaptureSource);
    }

    [Fact]
    public void CaptureSelected_NoSelection_ReturnsFalseAndSetsStatus()
    {
        var vm = Make(out _, out _, out _, out _);

        var ok = vm.CaptureSelected();

        Assert.False(ok);
        Assert.Null(vm.CapturedImage);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void CaptureSelected_CaptureThrows_ReturnsFalseAndSetsStatus_NoException()
    {
        var vm = Make(out _, out _, out _, out _);
        vm.SelectedSource = new CaptureSourceRow(
            new FakeCaptureSource { Behavior = () => throw new InvalidOperationException("boom") }, "X", "x", null);

        var ok = vm.CaptureSelected();

        Assert.False(ok);
        Assert.Null(vm.CapturedImage);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void TakeCapturedImage_TransfersOwnership_ClearsWithoutDisposing()
    {
        var vm = Make(out _, out _, out _, out _);
        vm.SelectedSource = new CaptureSourceRow(new FakeCaptureSource(), "A", "a", null);
        vm.CaptureSelected();

        var taken = vm.TakeCapturedImage();

        Assert.NotNull(taken);
        Assert.Null(vm.CapturedImage);
        Assert.False(vm.HasCapture);
        Assert.Equal(8, taken!.Width); // FakeCaptureSource returns an 8x8 bitmap; still usable (not disposed)
        taken.Dispose();
    }
}
```

- [ ] **Step 3: Run tests, verify failure**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~SourcePickerViewModelTests"`
Expected: FAIL (types `SourcePickerViewModel`, `SourceKind`, `CaptureSourceRow` do not exist).

- [ ] **Step 4: Create `SourceKind.cs`**

```csharp
namespace BotCapture.Core;

/// <summary>Which kind of source the picker is currently listing.</summary>
public enum SourceKind
{
    Window,
    Android,
}
```

- [ ] **Step 5: Create `CaptureSourceRow.cs`**

```csharp
namespace BotCapture.Core;

/// <summary>A picker list row: a capture source plus its display fields and an optional PNG thumbnail
/// (null when the thumbnail capture failed).</summary>
public sealed record CaptureSourceRow(ICaptureSource Source, string Label, string SubLabel, byte[]? ThumbnailPng);
```

- [ ] **Step 6: Create `SourcePickerViewModel.cs`**

```csharp
using System.Collections.ObjectModel;
using System.Drawing;
using AdbCore.Android;
using AdbCore.Screen;
using AdbCore.Targets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Drives the source picker: lists Win32 windows or connected Android devices (selectable via
/// <see cref="Kind"/>) as <see cref="CaptureSourceRow"/>s with thumbnails, and captures the selected
/// source. Capture failures surface as <see cref="StatusMessage"/>; an unavailable Android backend
/// surfaces as <see cref="UnavailableReason"/> — neither throws.</summary>
public partial class SourcePickerViewModel : ObservableObject
{
    private const int ThumbnailMaxDimension = 160;

    private readonly IWindowEnumerator _windows;
    private readonly IWindowCapture _windowCapture;
    private readonly IAdbDevices _devices;
    private readonly IAndroidDeviceConnector _connector;

    public SourcePickerViewModel(
        IWindowEnumerator windows, IWindowCapture windowCapture,
        IAdbDevices devices, IAndroidDeviceConnector connector)
    {
        _windows = windows;
        _windowCapture = windowCapture;
        _devices = devices;
        _connector = connector;
    }

    public ObservableCollection<CaptureSourceRow> Sources { get; } = new();

    [ObservableProperty] private CaptureSourceRow? _selectedSource;
    [ObservableProperty] private string? _statusMessage;

    /// <summary>Why the Android source can't be listed (adb missing / no devices); null when listing windows
    /// or when devices are present.</summary>
    [ObservableProperty] private string? _unavailableReason;

    /// <summary>Which source kind is shown. Changing it rebuilds the list.</summary>
    [ObservableProperty] private SourceKind _kind;

    /// <summary>The most recent successful capture; null until a capture succeeds.</summary>
    [ObservableProperty] private Bitmap? _capturedImage;

    partial void OnKindChanged(SourceKind value) => Refresh();
    partial void OnCapturedImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasCapture));

    /// <summary>Whether a capture is available to advance with.</summary>
    public bool HasCapture => CapturedImage is not null;

    /// <summary>The selected row's underlying source (handed to the confirm/session step), or null.</summary>
    public ICaptureSource? SelectedCaptureSource => SelectedSource?.Source;

    /// <summary>Rebuild the source list for the current <see cref="Kind"/>, clearing any prior capture so the
    /// picker doesn't show a stale preview after a refresh.</summary>
    public void Refresh()
    {
        StatusMessage = null;
        UnavailableReason = null;
        CapturedImage?.Dispose();
        CapturedImage = null;
        Sources.Clear();

        if (Kind == SourceKind.Window)
        {
            RefreshWindows();
        }
        else
        {
            RefreshAndroid();
        }
    }

    private void RefreshWindows()
    {
        foreach (var info in _windows.Enumerate())
        {
            var source = new WindowCaptureSource(info, _windowCapture);
            Sources.Add(new CaptureSourceRow(source, info.Title, info.ProcessName, TryThumbnail(source)));
        }
    }

    private void RefreshAndroid()
    {
        IReadOnlyList<AdbDeviceInfo> list;
        try
        {
            list = _devices.List();
        }
        catch (Exception ex)
        {
            UnavailableReason = $"adb not found on PATH — install Android platform-tools. ({ex.Message})";
            return;
        }

        foreach (var device in list)
        {
            ICaptureSource source;
            try
            {
                source = new AndroidCaptureSource(device.Serial, device.State, _connector.Connect(device.Serial));
            }
            catch
            {
                continue; // couldn't bind this device right now; skip it
            }
            Sources.Add(new CaptureSourceRow(source, device.Serial, device.State, TryThumbnail(source)));
        }

        if (Sources.Count == 0)
        {
            UnavailableReason = "No devices connected — run `adb devices`, then Refresh.";
        }
    }

    private static byte[]? TryThumbnail(ICaptureSource source)
    {
        try
        {
            using var bmp = source.Capture();
            return ThumbnailEncoder.ToPng(bmp, ThumbnailMaxDimension);
        }
        catch
        {
            return null; // unrenderable/offline source; the row still shows, just without a thumbnail
        }
    }

    /// <summary>Capture the selected source into <see cref="CapturedImage"/>. Returns false (with a
    /// <see cref="StatusMessage"/>) on no selection or capture failure; a failed capture leaves any prior
    /// <see cref="CapturedImage"/> untouched.</summary>
    public bool CaptureSelected()
    {
        if (SelectedSource is null)
        {
            StatusMessage = "Select a source first.";
            return false;
        }

        try
        {
            var captured = SelectedSource.Source.Capture();
            CapturedImage?.Dispose();
            CapturedImage = captured;
            StatusMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't capture that source: {ex.Message}";
            return false;
        }
    }

    /// <summary>Hands the current capture to the next step, transferring ownership: returns the bitmap and
    /// clears the field WITHOUT disposing it (the caller now owns and disposes it).</summary>
    public Bitmap? TakeCapturedImage()
    {
        var image = CapturedImage;
        CapturedImage = null; // relinquish without dispose; ownership moves to the caller
        return image;
    }
}
```

- [ ] **Step 7: Delete the old files**

```bash
git rm BotCapture.Core/WindowPickerViewModel.cs BotCapture.Core/WindowRow.cs
```

- [ ] **Step 8: Run tests, verify pass**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~SourcePickerViewModelTests"`
Expected: PASS (8 tests).

Note: the `BotCapture` WPF project will not compile yet (it still references `WindowPickerViewModel`/`SelectedWindow`); that is fixed in Tasks 7–8. The `--filter` run targets `BotCapture.Core.Tests`, which builds independently.

- [ ] **Step 9: Commit**

```bash
git add BotCapture.Core/SourceKind.cs BotCapture.Core/CaptureSourceRow.cs BotCapture.Core/SourcePickerViewModel.cs BotCapture.Core.Tests/SourcePickerViewModelTests.cs BotCapture.Core.Tests/Fakes.cs
git commit -m "Add SourcePickerViewModel: window + Android sources with toggle and availability reasons"
```

---

## Task 7: Rename the picker view + add the segmented toggle (XAML)

**Files:**
- Rename: `BotCapture/Views/WindowPickerView.xaml` → `BotCapture/Views/SourcePickerView.xaml`
- Rename: `BotCapture/Views/WindowPickerView.xaml.cs` → `BotCapture/Views/SourcePickerView.xaml.cs`

No unit test (WPF view); verified by build + manual run in Task 9.

- [ ] **Step 1: Rename the files (preserve history)**

```bash
git mv BotCapture/Views/WindowPickerView.xaml BotCapture/Views/SourcePickerView.xaml
git mv BotCapture/Views/WindowPickerView.xaml.cs BotCapture/Views/SourcePickerView.xaml.cs
```

- [ ] **Step 2: Replace `SourcePickerView.xaml` contents**

```xml
<UserControl x:Class="BotCapture.Views.SourcePickerView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:BotCapture.Views">
    <UserControl.Resources>
        <views:PngBytesToImageConverter x:Key="PngToImage" />
    </UserControl.Resources>
    <DockPanel Margin="8">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="Source:" VerticalAlignment="Center" Margin="0,0,8,0" />
            <RadioButton x:Name="WindowsToggle" Content="Windows" GroupName="SourceKind"
                         IsChecked="True" Checked="OnWindowsSelected"
                         Margin="0,0,4,0" Padding="8,2" />
            <RadioButton x:Name="AndroidToggle" Content="Android" GroupName="SourceKind"
                         Checked="OnAndroidSelected" Padding="8,2" />
            <Button Content="Refresh" Click="OnRefresh" Width="90" Margin="12,0,0,0" />
            <Button Content="Capture Selected" Click="OnCapture" Width="140" Margin="8,0,0,0" />
            <Button Content="Use This Capture →" Click="OnUseCapture" Width="150" Margin="8,0,0,0"
                    IsEnabled="{Binding HasCapture}" />
            <TextBlock Text="{Binding StatusMessage}" Foreground="{DynamicResource ErrorBrush}" Margin="12,4,0,0" />
        </StackPanel>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="2*" />
            </Grid.ColumnDefinitions>
            <Grid Grid.Column="0">
                <ListBox ItemsSource="{Binding Sources}"
                         SelectedItem="{Binding SelectedSource, Mode=TwoWay}">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal" Margin="2">
                                <Image Width="64" Height="40" Stretch="Uniform" Margin="0,0,8,0"
                                       Source="{Binding ThumbnailPng, Converter={StaticResource PngToImage}}" />
                                <StackPanel VerticalAlignment="Center">
                                    <TextBlock Text="{Binding Label}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />
                                    <TextBlock Text="{Binding SubLabel}" Foreground="{DynamicResource SecondaryTextBrush}" FontSize="11" />
                                </StackPanel>
                            </StackPanel>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
                <TextBlock Text="{Binding UnavailableReason}" TextWrapping="Wrap" Margin="8"
                           VerticalAlignment="Top" HorizontalAlignment="Center"
                           Foreground="{DynamicResource SecondaryTextBrush}" />
            </Grid>
            <Border Grid.Column="1" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" Margin="8,0,0,0">
                <Image x:Name="CapturedPreview" Stretch="Uniform" Margin="4" />
            </Border>
        </Grid>
    </DockPanel>
</UserControl>
```

(The unavailable hint overlays the empty list area; it is empty/blank whenever `UnavailableReason` is null, so no converter is needed.)

- [ ] **Step 3: Replace `SourcePickerView.xaml.cs` contents**

```csharp
using System.Windows;
using System.Windows.Controls;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class SourcePickerView : UserControl
{
    public SourcePickerView()
    {
        InitializeComponent();
    }

    private SourcePickerViewModel? Vm => DataContext as SourcePickerViewModel;

    private void OnWindowsSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null)
        {
            Vm.Kind = SourceKind.Window; // setter triggers Refresh()
            CapturedPreview.Source = null;
        }
    }

    private void OnAndroidSelected(object sender, RoutedEventArgs e)
    {
        if (Vm is not null)
        {
            Vm.Kind = SourceKind.Android; // setter triggers Refresh()
            CapturedPreview.Source = null;
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        Vm?.Refresh();
        CapturedPreview.Source = null;
    }

    private void OnCapture(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        if (!Vm.CaptureSelected())
        {
            CapturedPreview.Source = null; // clear stale image so the error status isn't paired with an old capture
            return;
        }

        if (Vm.CapturedImage is not null)
        {
            CapturedPreview.Source = BitmapInterop.ToImageSource(Vm.CapturedImage);
        }
    }

    /// <summary>Raised when the user accepts the current capture to proceed to region selection.</summary>
    public event EventHandler? CaptureAccepted;

    private void OnUseCapture(object sender, RoutedEventArgs e)
    {
        if (Vm?.HasCapture == true)
        {
            // The capture is handed off to region select; drop the in-place preview so returning to the
            // picker doesn't show a stale screenshot (the capture is gone and the button disabled).
            CapturedPreview.Source = null;
            CaptureAccepted?.Invoke(this, EventArgs.Empty);
        }
    }
}
```

- [ ] **Step 4: Commit (build happens in Task 8 once MainWindow is wired)**

```bash
git add BotCapture/Views/SourcePickerView.xaml BotCapture/Views/SourcePickerView.xaml.cs
git commit -m "Rename WindowPickerView to SourcePickerView; add Windows|Android toggle and unavailable hint"
```

---

## Task 8: Wire `MainWindow` to the new VM + `ICaptureSource`

**Files:**
- Modify: `BotCapture/MainWindow.xaml.cs`

- [ ] **Step 1: Update fields and the picker construction**

In `BotCapture/MainWindow.xaml.cs`, update the using block to include `using AdbCore.Android;` (add it next to the existing `using AdbCore.Screen;`).

Change the field declarations:

```csharp
    private readonly SourcePickerViewModel _pickerVm;
    private readonly SourcePickerView _pickerView;
```

Change `private IntPtr _sourceHandle;` to:

```csharp
    private ICaptureSource? _source;
```

In the constructor, change the picker construction:

```csharp
        _pickerVm = new SourcePickerViewModel(
            new Win32WindowEnumerator(),
            _capture,
            new AdvancedSharpAdbDevices(),
            new AdvancedSharpAdbDeviceConnector());
        _pickerView = new SourcePickerView { DataContext = _pickerVm };
        _pickerView.CaptureAccepted += OnCaptureAccepted;
```

And change the session VM construction (it no longer takes a capture):

```csharp
        _sessionVm = new SessionViewModel(_matcher, DefaultFolder());
```

- [ ] **Step 2: Update `OnCaptureAccepted`**

```csharp
    private void OnCaptureAccepted(object? sender, EventArgs e)
    {
        var image = _pickerVm.TakeCapturedImage();
        var source = _pickerVm.SelectedCaptureSource;
        if (image is null || source is null)
        {
            return;
        }

        _source = source;
        ShowRegion(new RegionSelectionViewModel(image));
    }
```

- [ ] **Step 3: Update `OnRegionConfirmed` + `ShowConfirm` signature**

```csharp
    private void OnRegionConfirmed(object? sender, System.Drawing.Bitmap crop)
        => ShowConfirm(crop, _source!, fileName: null, confidence: null);

    private void ShowConfirm(System.Drawing.Bitmap crop, ICaptureSource source, string? fileName, double? confidence)
    {
        _confirmVm?.Dispose();

        var saveFolder = _outputPath is not null ? Path.GetDirectoryName(_outputPath)! : _sessionVm.SaveFolder;
        _confirmVm = new PreviewConfirmViewModel(crop, source, _matcher, new CaptureSaver(saveFolder));
        if (_outputPath is not null)
        {
            _confirmVm.FileName = Path.GetFileName(_outputPath); // integrated: write exactly the requested file
        }
        if (fileName is not null)
        {
            _confirmVm.FileName = fileName;
        }
        if (confidence is not null)
        {
            _confirmVm.Confidence = confidence.Value;
        }

        var view = new PreviewConfirmView();
        view.Saved += OnConfirmSaved;
        view.RetakeRequested += (_, _) =>
        {
            DisposeConfirm();
            if (_regionVm is not null)
            {
                SetContent(BuildRegionView(_regionVm));
            }
            else
            {
                ReturnHome();
            }
        };
        view.Bind(_confirmVm);
        SetContent(view);
    }
```

- [ ] **Step 4: Update `OnConfirmSaved` (the `Add` call) and `StartReEdit`**

In `OnConfirmSaved`, change the `_sessionVm.Add(...)` call:

```csharp
        else
        {
            _sessionVm.Add(path, confidence, _source!);
        }
```

Replace `StartReEdit`:

```csharp
    private void StartReEdit(SessionRow row)
    {
        var crop = LoadDetached(row.FilePath);
        if (crop is null)
        {
            return; // unreadable file; stay on the session panel
        }

        _editingRow = row;
        _source = row.Source;
        DisposeRegion(); // no region step on re-edit
        ShowConfirm(crop, row.Source, row.FileName, row.Confidence);
    }
```

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, 0 errors. (Fix any missed `_sourceHandle`/`SelectedWindow`/`WindowPickerView` references the compiler flags.)

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test ADB.slnx`
Expected: PASS — all projects, including the rewritten `BotCapture.Core.Tests`.

- [ ] **Step 7: Commit**

```bash
git add BotCapture/MainWindow.xaml.cs
git commit -m "Wire BotCapture MainWindow to SourcePickerViewModel and ICaptureSource"
```

---

## Task 9: Full verification + manual run note

**Files:** none (verification only)

- [ ] **Step 1: Clean build + full test run**

Run: `dotnet build ADB.slnx` then `dotnet test ADB.slnx`
Expected: Build succeeded; all tests PASS. Capture the counts for the PR body.

- [ ] **Step 2: Grep for leftover references**

Run: `git grep -n "WindowPickerView\|WindowPickerViewModel\|WindowRow\|_sourceHandle\|SelectedWindow"`
Expected: no matches (all renamed/removed).

- [ ] **Step 3: Record the manual-verification checklist for the PR (the user runs this)**

The user will verify visually (this is a visual slice). Note in the PR body:
- Launch `dotnet run --project BotCapture`. Confirm the `Windows | Android` toggle renders and is keyboard-focusable.
- With no device / no adb: switch to Android → the unavailable hint shows ("No devices connected…" or "adb not found…"); Capture is a no-op with a clear status.
- With a device connected: switch to Android → the device row + thumbnail appears; Capture Selected shows the framebuffer; Use This Capture → crop → Test Match (live) reports a score; Save writes the PNG.
- Windows tab still behaves exactly as before.
- From BotBuilder: select an Android Find Image action, click **Capture** → BotCapture opens, switch to Android, capture/crop/save → the action's image-path field is populated.

- [ ] **Step 4: Push the branch and open the PR (parked for user verify + merge)**

```bash
git push -u origin feat/botcapture-android-source
gh pr create --title "BotCapture: capture templates from Android devices" --body "<summary + manual checklist + test counts>"
```

---

## Self-Review Notes

- **Spec coverage:** §1 abstraction → Task 1; §2 enumeration/connector/availability → Tasks 2 & 6; §3 toggle UI → Task 7; §4 wiring → Tasks 3–5, 8; §5 correctness (framebuffer) → encoded in `AndroidCaptureSource` (Task 1) + verified by the runtime-match note; §6 testing → Tasks 1, 4, 5, 6.
- **Type consistency:** `ICaptureSource { Label, SubLabel, Capture() }`, `CaptureSourceRow(Source, Label, SubLabel, ThumbnailPng)`, `SourcePickerViewModel.{ Sources, SelectedSource, SelectedCaptureSource, Kind, UnavailableReason, StatusMessage, CapturedImage, HasCapture, Refresh(), CaptureSelected(), TakeCapturedImage() }`, `SessionViewModel(matcher, saveFolder)` + `Add(path, confidence, ICaptureSource)`, `PreviewConfirmViewModel(crop, ICaptureSource, matcher, saver)`, `SessionRow(filePath, confidence, ICaptureSource)` — names are consistent across all tasks.
- **No placeholders:** every code/test step contains full content.
