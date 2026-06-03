# M6a — BotCapture Scaffold + Window Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the BotCapture WPF tool's first screen — enumerate visible windows, show them with thumbnails, and capture the selected window's client area — on a new three-project scaffold, reusing AdbCore's capture infra.

**Architecture:** Add a list-shaped window enumerator to `AdbCore.Targets` (sibling of the existing `Win32WindowResolver`). Create `BotCapture` (WPF shell), `BotCapture.Core` (testable VM/services, CommunityToolkit.Mvvm), and `BotCapture.Core.Tests` (xUnit), mirroring the BotBuilder split. The picker VM depends on `IWindowEnumerator` + `IWindowCapture` (constructor-injected; faked in tests); WPF views and the Bitmap→ImageSource bridges are verified visually.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm 8.4.2, System.Drawing.Common (transitive via AdbCore), xUnit. Per `Docs/Specs/2026-06-03-m6-botcapture-design.md` §3–4.

---

## File Structure

**AdbCore (new):**
- `AdbCore/Targets/WindowInfo.cs` — `readonly record struct WindowInfo(IntPtr Handle, string Title, string ProcessName)`.
- `AdbCore/Targets/IWindowEnumerator.cs` — `IReadOnlyList<WindowInfo> Enumerate()`.
- `AdbCore/Targets/Win32WindowEnumerator.cs` — P/Invoke adapter + static `ShouldInclude` predicate.
- `AdbCore.Tests/Targets/Win32WindowEnumeratorTests.cs` — unit tests for `ShouldInclude`.

**BotCapture.Core (new project):**
- `BotCapture.Core/BotCapture.Core.csproj`
- `BotCapture.Core/ThumbnailEncoder.cs` — downscale + PNG-encode a capture.
- `BotCapture.Core/WindowRow.cs` — list-row record (`WindowInfo` + thumbnail bytes).
- `BotCapture.Core/WindowPickerViewModel.cs` — enumerate→rows, capture selected.

**BotCapture.Core.Tests (new project):**
- `BotCapture.Core.Tests/BotCapture.Core.Tests.csproj`
- `BotCapture.Core.Tests/Fakes.cs` — `FakeWindowEnumerator`, `FakeWindowCapture`.
- `BotCapture.Core.Tests/ThumbnailEncoderTests.cs`
- `BotCapture.Core.Tests/WindowPickerViewModelTests.cs`

**BotCapture (new WPF project):**
- `BotCapture/BotCapture.csproj`, `BotCapture/app.manifest`
- `BotCapture/App.xaml(.cs)`, `BotCapture/MainWindow.xaml(.cs)`
- `BotCapture/Views/WindowPickerView.xaml(.cs)`
- `BotCapture/Views/PngBytesToImageConverter.cs`, `BotCapture/Views/BitmapInterop.cs`

**Solution:** add the three projects to `ADB.slnx`.

---

## Task 1: Window enumerator in AdbCore

**Files:**
- Create: `AdbCore/Targets/WindowInfo.cs`, `AdbCore/Targets/IWindowEnumerator.cs`, `AdbCore/Targets/Win32WindowEnumerator.cs`
- Test: `AdbCore.Tests/Targets/Win32WindowEnumeratorTests.cs`

> The P/Invoke loop in `Win32WindowEnumerator.Enumerate()` is not unit-tested — consistent with the
> existing `Win32WindowResolver`/`Win32WindowCapture` adapters (real OS calls; verified via the picker
> visually). The pure inclusion predicate is extracted to `ShouldInclude` and IS unit-tested.

- [ ] **Step 1: Write the failing test**

`AdbCore.Tests/Targets/Win32WindowEnumeratorTests.cs`:

```csharp
using AdbCore.Targets;

namespace AdbCore.Tests.Targets;

public class Win32WindowEnumeratorTests
{
    [Theory]
    [InlineData(true, 5, true)]    // visible + titled -> include
    [InlineData(false, 5, false)]  // hidden -> exclude
    [InlineData(true, 0, false)]   // untitled -> exclude
    [InlineData(false, 0, false)]  // hidden + untitled -> exclude
    public void ShouldInclude_RequiresVisibleAndNonEmptyTitle(bool isVisible, int titleLength, bool expected)
    {
        Assert.Equal(expected, Win32WindowEnumerator.ShouldInclude(isVisible, titleLength));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~Win32WindowEnumeratorTests"`
Expected: FAIL — build error, `Win32WindowEnumerator` / `WindowInfo` / `IWindowEnumerator` do not exist.

- [ ] **Step 3: Create `WindowInfo` and `IWindowEnumerator`**

`AdbCore/Targets/WindowInfo.cs`:

```csharp
namespace AdbCore.Targets;

/// <summary>A visible top-level window discovered by <see cref="IWindowEnumerator"/>. <see cref="Handle"/>
/// is the HWND, <see cref="Title"/> the window text, and <see cref="ProcessName"/> the owning process
/// (empty when it can't be resolved).</summary>
public readonly record struct WindowInfo(IntPtr Handle, string Title, string ProcessName);
```

`AdbCore/Targets/IWindowEnumerator.cs`:

```csharp
namespace AdbCore.Targets;

/// <summary>Enumerates visible top-level windows suitable for capture/selection in the UI.</summary>
public interface IWindowEnumerator
{
    IReadOnlyList<WindowInfo> Enumerate();
}
```

- [ ] **Step 4: Create `Win32WindowEnumerator`**

`AdbCore/Targets/Win32WindowEnumerator.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AdbCore.Targets;

/// <summary>Win32 implementation of <see cref="IWindowEnumerator"/>. Lists visible top-level windows with
/// a non-empty title via <c>EnumWindows</c>, resolving each window's owning process name (best-effort).</summary>
public sealed class Win32WindowEnumerator : IWindowEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>A window qualifies for the list when it is visible and has a non-empty title.</summary>
    public static bool ShouldInclude(bool isVisible, int titleLength) => isVisible && titleLength > 0;

    public IReadOnlyList<WindowInfo> Enumerate()
    {
        var results = new List<WindowInfo>();
        EnumWindows((hWnd, _) =>
        {
            var length = GetWindowTextLength(hWnd);
            if (!ShouldInclude(IsWindowVisible(hWnd), length))
            {
                return true; // skip, keep enumerating
            }

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            results.Add(new WindowInfo(hWnd, sb.ToString(), ResolveProcessName(hWnd)));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    private static string ResolveProcessName(IntPtr hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == 0)
            {
                return string.Empty;
            }

            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty; // process exited between enumeration and access, or access denied
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test AdbCore.Tests/AdbCore.Tests.csproj --filter "FullyQualifiedName~Win32WindowEnumeratorTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add AdbCore/Targets/WindowInfo.cs AdbCore/Targets/IWindowEnumerator.cs AdbCore/Targets/Win32WindowEnumerator.cs AdbCore.Tests/Targets/Win32WindowEnumeratorTests.cs
git commit -m "feat(core): add IWindowEnumerator + Win32WindowEnumerator (visible-window listing)"
```

---

## Task 2: Scaffold BotCapture projects

Creates the three projects so later tasks have a home. Ends on a buildable, runnable (empty) WPF app.

**Files:**
- Create: `BotCapture.Core/BotCapture.Core.csproj`, `BotCapture.Core.Tests/BotCapture.Core.Tests.csproj`,
  `BotCapture/BotCapture.csproj`, `BotCapture/app.manifest`, `BotCapture/App.xaml`, `BotCapture/App.xaml.cs`,
  `BotCapture/MainWindow.xaml`, `BotCapture/MainWindow.xaml.cs`
- Modify: `ADB.slnx`

- [ ] **Step 1: Create `BotCapture.Core.csproj`**

`BotCapture.Core/BotCapture.Core.csproj` (System.Drawing.Common flows transitively from AdbCore — no explicit reference needed):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\AdbCore\AdbCore.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>BotCapture.Core</RootNamespace>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Create `BotCapture.Core.Tests.csproj`**

`BotCapture.Core.Tests/BotCapture.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\BotCapture.Core\BotCapture.Core.csproj" />
    <ProjectReference Include="..\AdbCore\AdbCore.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create the WPF shell project + manifest**

`BotCapture/BotCapture.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\BotCapture.Core\BotCapture.Core.csproj" />
    <ProjectReference Include="..\AdbCore\AdbCore.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <RootNamespace>BotCapture</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>

</Project>
```

`BotCapture/app.manifest` (Per-Monitor-V2 DPI so capture/overlay share the target's pixel space — the WPF
equivalent of the runner's `SetProcessDpiAwarenessContext`, per spec §7):

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/PM</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 4: Create App + minimal MainWindow**

`BotCapture/App.xaml`:

```xml
<Application x:Class="BotCapture.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
```

`BotCapture/App.xaml.cs`:

```csharp
using System.Windows;

namespace BotCapture;

public partial class App : Application
{
}
```

`BotCapture/MainWindow.xaml` (placeholder; filled in Task 5):

```xml
<Window x:Class="BotCapture.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="BotCapture" Height="640" Width="900">
    <Grid />
</Window>
```

`BotCapture/MainWindow.xaml.cs`:

```csharp
using System.Windows;

namespace BotCapture;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 5: Add the three projects to `ADB.slnx`**

Run:

```bash
dotnet sln ADB.slnx add BotCapture.Core/BotCapture.Core.csproj BotCapture.Core.Tests/BotCapture.Core.Tests.csproj BotCapture/BotCapture.csproj
```

Expected: "Project ... added to the solution." three times.

- [ ] **Step 6: Build the solution**

Run: `dotnet build ADB.slnx -c Debug --nologo`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add ADB.slnx BotCapture BotCapture.Core BotCapture.Core.Tests
git commit -m "scaffold(capture): add BotCapture, BotCapture.Core, BotCapture.Core.Tests projects (PerMonitorV2 DPI)"
```

---

## Task 3: ThumbnailEncoder

Pure helper that downscales a capture and PNG-encodes it for list display.

**Files:**
- Create: `BotCapture.Core/ThumbnailEncoder.cs`
- Test: `BotCapture.Core.Tests/ThumbnailEncoderTests.cs`

- [ ] **Step 1: Write the failing test**

`BotCapture.Core.Tests/ThumbnailEncoderTests.cs`:

```csharp
using System.Drawing;
using System.IO;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class ThumbnailEncoderTests
{
    [Fact]
    public void ToPng_DownscalesLongSideToMaxDimension_PreservingAspect()
    {
        using var src = new Bitmap(200, 100);

        var bytes = ThumbnailEncoder.ToPng(src, 50);

        Assert.NotEmpty(bytes);
        using var decoded = new Bitmap(new MemoryStream(bytes));
        Assert.Equal(50, decoded.Width);
        Assert.Equal(25, decoded.Height);
    }

    [Fact]
    public void ToPng_NeverUpscales_SmallSourceUnchanged()
    {
        using var src = new Bitmap(30, 20);

        using var decoded = new Bitmap(new MemoryStream(ThumbnailEncoder.ToPng(src, 160)));

        Assert.Equal(30, decoded.Width);
        Assert.Equal(20, decoded.Height);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test BotCapture.Core.Tests/BotCapture.Core.Tests.csproj --filter "FullyQualifiedName~ThumbnailEncoderTests"`
Expected: FAIL — `ThumbnailEncoder` does not exist.

- [ ] **Step 3: Implement `ThumbnailEncoder`**

`BotCapture.Core/ThumbnailEncoder.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace BotCapture.Core;

/// <summary>Encodes a capture into a small PNG thumbnail for list display. Pure (bytes in/out, no UI
/// dependency) so the view can turn the bytes into a WPF image.</summary>
public static class ThumbnailEncoder
{
    /// <summary>Downscales <paramref name="source"/> to fit within <paramref name="maxDimension"/> px on
    /// its longest side (preserving aspect ratio; never upscaling) and returns PNG-encoded bytes.</summary>
    public static byte[] ToPng(Bitmap source, int maxDimension)
    {
        var longest = Math.Max(source.Width, source.Height);
        var scale = Math.Min(1.0, maxDimension / (double)longest);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var thumb = new Bitmap(width, height);
        using (var g = Graphics.FromImage(thumb))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, width, height);
        }

        using var stream = new MemoryStream();
        thumb.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test BotCapture.Core.Tests/BotCapture.Core.Tests.csproj --filter "FullyQualifiedName~ThumbnailEncoderTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add BotCapture.Core/ThumbnailEncoder.cs BotCapture.Core.Tests/ThumbnailEncoderTests.cs
git commit -m "feat(capture): add ThumbnailEncoder (downscale + PNG-encode captures)"
```

---

## Task 4: WindowRow + WindowPickerViewModel

The picker's logic: enumerate→rows (with thumbnails), capture the selected window, surface failures as status.

**Files:**
- Create: `BotCapture.Core/WindowRow.cs`, `BotCapture.Core/WindowPickerViewModel.cs`
- Test: `BotCapture.Core.Tests/Fakes.cs`, `BotCapture.Core.Tests/WindowPickerViewModelTests.cs`

- [ ] **Step 1: Write the fakes**

`BotCapture.Core.Tests/Fakes.cs`:

```csharp
using System.Drawing;
using AdbCore.Screen;
using AdbCore.Targets;

namespace BotCapture.Core.Tests;

internal sealed class FakeWindowEnumerator : IWindowEnumerator
{
    public IReadOnlyList<WindowInfo> Result = Array.Empty<WindowInfo>();
    public IReadOnlyList<WindowInfo> Enumerate() => Result;
}

internal sealed class FakeWindowCapture : IWindowCapture
{
    public List<(IntPtr Handle, ScreenCaptureMethod Method)> Calls = new();

    /// <summary>Optional per-call behavior; default returns a tiny bitmap. Set to throw to simulate
    /// an unrenderable window.</summary>
    public Func<IntPtr, Bitmap>? Behavior;

    public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method)
    {
        Calls.Add((windowHandle, method));
        return Behavior is not null ? Behavior(windowHandle) : new Bitmap(8, 8);
    }
}
```

- [ ] **Step 2: Write the failing tests**

`BotCapture.Core.Tests/WindowPickerViewModelTests.cs`:

```csharp
using AdbCore.Screen;
using AdbCore.Targets;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class WindowPickerViewModelTests
{
    private static WindowPickerViewModel Make(out FakeWindowEnumerator enumerator, out FakeWindowCapture capture)
    {
        enumerator = new FakeWindowEnumerator();
        capture = new FakeWindowCapture();
        return new WindowPickerViewModel(enumerator, capture);
    }

    [Fact]
    public void Refresh_MapsEnumeratedWindowsToRowsInOrder()
    {
        var vm = Make(out var enumerator, out _);
        enumerator.Result = new[]
        {
            new WindowInfo((IntPtr)1, "Alpha", "alpha"),
            new WindowInfo((IntPtr)2, "Beta", "beta"),
        };

        vm.Refresh();

        Assert.Equal(2, vm.Windows.Count);
        Assert.Equal("Alpha", vm.Windows[0].Title);
        Assert.Equal("Beta", vm.Windows[1].Title);
        Assert.NotNull(vm.Windows[0].ThumbnailPng);
    }

    [Fact]
    public void Refresh_NoWindows_ProducesNoRows()
    {
        var vm = Make(out _, out _);

        vm.Refresh();

        Assert.Empty(vm.Windows);
    }

    [Fact]
    public void Refresh_ThumbnailCaptureThrows_RowStillAddedWithNullThumbnail()
    {
        var vm = Make(out var enumerator, out var capture);
        enumerator.Result = new[] { new WindowInfo((IntPtr)1, "Alpha", "alpha") };
        capture.Behavior = _ => throw new InvalidOperationException("unrenderable");

        vm.Refresh();

        Assert.Single(vm.Windows);
        Assert.Null(vm.Windows[0].ThumbnailPng);
    }

    [Fact]
    public void CaptureSelected_UsesSelectedHandleAndPrintWindow_SetsCapturedImage()
    {
        var vm = Make(out _, out var capture);
        vm.SelectedWindow = new WindowRow(new WindowInfo((IntPtr)42, "Game", "game"), null);

        var ok = vm.CaptureSelected();

        Assert.True(ok);
        Assert.NotNull(vm.CapturedImage);
        var last = capture.Calls[^1];
        Assert.Equal((IntPtr)42, last.Handle);
        Assert.Equal(ScreenCaptureMethod.Auto, last.Method);
    }

    [Fact]
    public void CaptureSelected_NoSelection_ReturnsFalseAndSetsStatus()
    {
        var vm = Make(out _, out _);

        var ok = vm.CaptureSelected();

        Assert.False(ok);
        Assert.Null(vm.CapturedImage);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void CaptureSelected_CaptureThrows_ReturnsFalseAndSetsStatus_NoException()
    {
        var vm = Make(out _, out var capture);
        capture.Behavior = _ => throw new InvalidOperationException("boom");
        vm.SelectedWindow = new WindowRow(new WindowInfo((IntPtr)7, "X", "x"), null);

        var ok = vm.CaptureSelected();

        Assert.False(ok);
        Assert.Null(vm.CapturedImage);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test BotCapture.Core.Tests/BotCapture.Core.Tests.csproj --filter "FullyQualifiedName~WindowPickerViewModelTests"`
Expected: FAIL — `WindowRow` / `WindowPickerViewModel` do not exist.

- [ ] **Step 4: Implement `WindowRow`**

`BotCapture.Core/WindowRow.cs`:

```csharp
using AdbCore.Targets;

namespace BotCapture.Core;

/// <summary>A window-picker list row: the enumerated window plus an optional PNG thumbnail
/// (null when that window's thumbnail capture failed).</summary>
public sealed record WindowRow(WindowInfo Info, byte[]? ThumbnailPng)
{
    public string Title => Info.Title;
    public string ProcessName => Info.ProcessName;
}
```

- [ ] **Step 5: Implement `WindowPickerViewModel`**

`BotCapture.Core/WindowPickerViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Drawing;
using AdbCore.Screen;
using AdbCore.Targets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Drives the window-picker screen: enumerates visible windows into rows (each with a thumbnail),
/// and captures the selected window's client area. Capture failures surface as <see cref="StatusMessage"/>
/// rather than exceptions.</summary>
public partial class WindowPickerViewModel : ObservableObject
{
    private const int ThumbnailMaxDimension = 160;

    private readonly IWindowEnumerator _enumerator;
    private readonly IWindowCapture _capture;

    public WindowPickerViewModel(IWindowEnumerator enumerator, IWindowCapture capture)
    {
        _enumerator = enumerator;
        _capture = capture;
    }

    public ObservableCollection<WindowRow> Windows { get; } = new();

    [ObservableProperty] private WindowRow? _selectedWindow;
    [ObservableProperty] private string? _statusMessage;

    /// <summary>The most recent successful client-area capture of the selected window; null until a
    /// capture succeeds. Handed off to the region-select stage (M6b).</summary>
    public Bitmap? CapturedImage { get; private set; }

    /// <summary>Re-enumerate visible windows and rebuild rows (capturing a thumbnail per row).</summary>
    public void Refresh()
    {
        StatusMessage = null;
        Windows.Clear();
        foreach (var info in _enumerator.Enumerate())
        {
            Windows.Add(new WindowRow(info, TryCaptureThumbnail(info.Handle)));
        }
    }

    /// <summary>Capture the selected window's client area into <see cref="CapturedImage"/>.
    /// Returns false (with a <see cref="StatusMessage"/>) on no selection or capture failure.</summary>
    public bool CaptureSelected()
    {
        if (SelectedWindow is null)
        {
            StatusMessage = "Select a window first.";
            return false;
        }

        try
        {
            CapturedImage?.Dispose();
            CapturedImage = _capture.Capture(SelectedWindow.Info.Handle, ScreenCaptureMethod.Auto);
            StatusMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            CapturedImage = null;
            StatusMessage = $"Couldn't capture that window: {ex.Message}";
            return false;
        }
    }

    private byte[]? TryCaptureThumbnail(IntPtr handle)
    {
        try
        {
            using var bmp = _capture.Capture(handle, ScreenCaptureMethod.Auto);
            return ThumbnailEncoder.ToPng(bmp, ThumbnailMaxDimension);
        }
        catch
        {
            return null; // window may be unrenderable; the row still shows, just without a thumbnail
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test BotCapture.Core.Tests/BotCapture.Core.Tests.csproj --filter "FullyQualifiedName~WindowPickerViewModelTests"`
Expected: PASS (6 tests).

- [ ] **Step 7: Commit**

```bash
git add BotCapture.Core/WindowRow.cs BotCapture.Core/WindowPickerViewModel.cs BotCapture.Core.Tests/Fakes.cs BotCapture.Core.Tests/WindowPickerViewModelTests.cs
git commit -m "feat(capture): add WindowPickerViewModel + WindowRow (enumerate, thumbnail, capture)"
```

---

## Task 5: WPF picker UI (visual verification)

Wires the VM to a real window-picker screen. No unit tests — verified by running the app (user does visual verification per the project's rhythm).

**Files:**
- Create: `BotCapture/Views/PngBytesToImageConverter.cs`, `BotCapture/Views/BitmapInterop.cs`,
  `BotCapture/Views/WindowPickerView.xaml`, `BotCapture/Views/WindowPickerView.xaml.cs`
- Modify: `BotCapture/MainWindow.xaml`, `BotCapture/MainWindow.xaml.cs`

- [ ] **Step 1: Add the PNG-bytes→image converter**

`BotCapture/Views/PngBytesToImageConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace BotCapture.Views;

/// <summary>Binds PNG bytes (a thumbnail) to a frozen WPF image; null/empty -> null.</summary>
public sealed class PngBytesToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Add the Bitmap→ImageSource bridge**

`BotCapture/Views/BitmapInterop.cs`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace BotCapture.Views;

/// <summary>Bridges a System.Drawing.Bitmap (capture output) to a WPF image via an in-memory PNG.</summary>
internal static class BitmapInterop
{
    public static BitmapImage ToImageSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
```

- [ ] **Step 3: Add the picker view**

`BotCapture/Views/WindowPickerView.xaml`:

```xml
<UserControl x:Class="BotCapture.Views.WindowPickerView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:BotCapture.Views">
    <UserControl.Resources>
        <views:PngBytesToImageConverter x:Key="PngToImage" />
    </UserControl.Resources>
    <DockPanel Margin="8">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <Button Content="Refresh" Click="OnRefresh" Width="90" />
            <Button Content="Capture Selected" Click="OnCapture" Width="140" Margin="8,0,0,0" />
            <TextBlock Text="{Binding StatusMessage}" Foreground="DarkRed" Margin="12,4,0,0" />
        </StackPanel>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="2*" />
            </Grid.ColumnDefinitions>
            <ListBox Grid.Column="0"
                     ItemsSource="{Binding Windows}"
                     SelectedItem="{Binding SelectedWindow, Mode=TwoWay}">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal" Margin="2">
                            <Image Width="64" Height="40" Stretch="Uniform" Margin="0,0,8,0"
                                   Source="{Binding ThumbnailPng, Converter={StaticResource PngToImage}}" />
                            <StackPanel VerticalAlignment="Center">
                                <TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />
                                <TextBlock Text="{Binding ProcessName}" Foreground="Gray" FontSize="11" />
                            </StackPanel>
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
            <Border Grid.Column="1" BorderBrush="LightGray" BorderThickness="1" Margin="8,0,0,0">
                <Image x:Name="CapturedPreview" Stretch="Uniform" Margin="4" />
            </Border>
        </Grid>
    </DockPanel>
</UserControl>
```

`BotCapture/Views/WindowPickerView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class WindowPickerView : UserControl
{
    public WindowPickerView()
    {
        InitializeComponent();
    }

    private WindowPickerViewModel? Vm => DataContext as WindowPickerViewModel;

    private void OnRefresh(object sender, RoutedEventArgs e) => Vm?.Refresh();

    private void OnCapture(object sender, RoutedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        if (Vm.CaptureSelected() && Vm.CapturedImage is not null)
        {
            CapturedPreview.Source = BitmapInterop.ToImageSource(Vm.CapturedImage);
        }
    }
}
```

- [ ] **Step 4: Host the view + wire the composition root in MainWindow**

`BotCapture/MainWindow.xaml`:

```xml
<Window x:Class="BotCapture.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:BotCapture.Views"
        Title="BotCapture" Height="640" Width="900">
    <Grid>
        <views:WindowPickerView x:Name="Picker" />
    </Grid>
</Window>
```

`BotCapture/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using AdbCore.Screen;
using AdbCore.Targets;
using BotCapture.Core;

namespace BotCapture;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new WindowPickerViewModel(new Win32WindowEnumerator(), new Win32WindowCapture());
        Picker.DataContext = viewModel;
        viewModel.Refresh();
    }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build ADB.slnx -c Debug --nologo`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add BotCapture/Views BotCapture/MainWindow.xaml BotCapture/MainWindow.xaml.cs
git commit -m "feat(capture): window-picker UI (list+thumbnails, capture selected window)"
```

---

## Task 6: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build ADB.slnx -c Debug --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run the whole test suite**

Run: `dotnet test ADB.slnx -c Debug --nologo --no-build`
Expected: all test projects PASS, 0 failures. The new tests add: AdbCore +4 (`Win32WindowEnumeratorTests`),
BotCapture.Core +8 (`ThumbnailEncoderTests` 2, `WindowPickerViewModelTests` 6). Baseline was AdbCore 222 /
BotBuilder.Core 103 / BotRunner 19; expect AdbCore 226 and a new BotCapture.Core.Tests 8.

- [ ] **Step 3: Manual run (user visual verification)**

Run: `dotnet run --project BotCapture/BotCapture.csproj -c Debug`
Expected: a BotCapture window opens listing visible windows with thumbnails, process names, and titles.
Selecting a window and clicking **Capture Selected** shows that window's client-area capture in the right
pane. **Refresh** re-lists. Selecting an unrenderable window shows a red status message, not a crash.

> Hand off to the user for visual confirmation of the picker before opening the PR.
