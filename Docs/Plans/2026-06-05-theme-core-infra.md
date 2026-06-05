# Theme Core + Infrastructure Implementation Plan (Slice 1 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `AdbUi.Theme` WPF class library — the shared foundation that provides Light/Dark/High-Contrast brush dictionaries, theme selection/resolution/persistence logic, OS-theme detection, and a `ThemeManager` that swaps the active theme at runtime — with full unit-test coverage of the non-WPF logic.

**Architecture:** A new WPF class library `AdbUi.Theme` referenced later by BotBuilder and BotCapture (slices 2 & 3). Pure logic (`ThemeResolver`, `AppSettings`/`JsonSettingsStore`) is unit-tested directly. WPF-bound work is isolated behind seams: `IOsThemeProbe` (OS theme + change events) and `IThemeApplier` (merges/swaps `ResourceDictionary` objects), each with a fake for tests and a live Win32/WPF implementation. `ThemeManager` orchestrates selection → resolution → application → persistence and is unit-tested against the fakes. Three brush dictionaries and one theme-agnostic `Controls.xaml` (implicit control styles, all `{DynamicResource}`) provide the actual visuals; nothing consumes them yet (that is slices 2 & 3).

**Tech Stack:** .NET `net10.0-windows`, WPF (`UseWPF`), C# nullable enabled, `System.Text.Json` (built in), xUnit 2.9.3.

**Spec:** `Docs/Specs/2026-06-05-theming-system-design.md`

---

## File Structure

**New project `AdbUi.Theme/` (WPF class library):**
- `AdbUi.Theme.csproj` — `UseWPF`, `net10.0-windows`.
- `AppTheme.cs` — `enum AppTheme { Light, Dark, HighContrast }` (effective theme).
- `ThemeSelection.cs` — `enum ThemeSelection { System, Light, Dark, HighContrast }` (user choice).
- `ThemeResolver.cs` — pure resolution of selection + OS theme → effective theme.
- `AppSettings.cs` — settings record (general bag; v1 holds `Theme`).
- `ISettingsStore.cs` / `JsonSettingsStore.cs` — load/save settings JSON, resilient to missing/corrupt.
- `SettingsPaths.cs` — resolves the `%AppData%/ADB/settings.json` path.
- `IOsThemeProbe.cs` / `Win32OsThemeProbe.cs` — current OS theme + change event.
- `IThemeApplier.cs` / `ResourceDictionaryThemeApplier.cs` — applies a theme's brush dictionary to the live WPF resources.
- `ThemeManager.cs` — orchestrates everything; the public entry point apps use.
- `Themes/Light.xaml`, `Themes/Dark.xaml`, `Themes/HighContrast.xaml` — brush keys only.
- `Themes/Controls.xaml` — theme-agnostic implicit control styles.

**New project `AdbUi.Theme.Tests/` (xUnit):**
- `AdbUi.Theme.Tests.csproj`.
- `ThemeResolverTests.cs`, `JsonSettingsStoreTests.cs`, `ThemeManagerTests.cs`.
- `Fakes/FakeOsThemeProbe.cs`, `Fakes/FakeThemeApplier.cs`, `Fakes/FakeSettingsStore.cs`.

**Modified:**
- `ADB.slnx` — register the two new projects.

---

## Task 1: Create the `AdbUi.Theme` WPF class library

**Files:**
- Create: `AdbUi.Theme/AdbUi.Theme.csproj`
- Modify: `ADB.slnx`

- [ ] **Step 1: Create the project file**

Create `AdbUi.Theme/AdbUi.Theme.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <RootNamespace>AdbUi.Theme</RootNamespace>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Register the project in the solution**

In `ADB.slnx`, add this line inside `<Solution>` (keep alphabetical-ish grouping, place after the `AdbCore` lines):

```xml
  <Project Path="AdbUi.Theme/AdbUi.Theme.csproj" />
```

- [ ] **Step 3: Build to verify the empty project compiles**

Run: `dotnet build AdbUi.Theme/AdbUi.Theme.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add AdbUi.Theme/AdbUi.Theme.csproj ADB.slnx
git commit -m "build(theme): add empty AdbUi.Theme WPF class library"
```

---

## Task 2: Create the `AdbUi.Theme.Tests` project

**Files:**
- Create: `AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj`
- Modify: `ADB.slnx`

- [ ] **Step 1: Create the test project file**

Create `AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj` (mirrors `BotBuilder.Core.Tests`):

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
    <ProjectReference Include="..\AdbUi.Theme\AdbUi.Theme.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Register in the solution**

In `ADB.slnx`, add inside `<Solution>` (place before the `AdbUi.Theme` project line):

```xml
  <Project Path="AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj" />
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj`
Expected: Build succeeded (0 tests yet).

- [ ] **Step 4: Commit**

```bash
git add AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj ADB.slnx
git commit -m "build(theme): add AdbUi.Theme.Tests project"
```

---

## Task 3: Theme enums

**Files:**
- Create: `AdbUi.Theme/AppTheme.cs`
- Create: `AdbUi.Theme/ThemeSelection.cs`

- [ ] **Step 1: Create `AppTheme`**

Create `AdbUi.Theme/AppTheme.cs`:

```csharp
namespace AdbUi.Theme;

/// <summary>The theme actually applied to the UI (no "System" — that has been resolved away).</summary>
public enum AppTheme
{
    Light,
    Dark,
    HighContrast,
}
```

- [ ] **Step 2: Create `ThemeSelection`**

Create `AdbUi.Theme/ThemeSelection.cs`:

```csharp
namespace AdbUi.Theme;

/// <summary>The user's theme choice. <see cref="System"/> means "follow the OS theme".</summary>
public enum ThemeSelection
{
    System,
    Light,
    Dark,
    HighContrast,
}
```

- [ ] **Step 3: Build**

Run: `dotnet build AdbUi.Theme/AdbUi.Theme.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add AdbUi.Theme/AppTheme.cs AdbUi.Theme/ThemeSelection.cs
git commit -m "feat(theme): add AppTheme and ThemeSelection enums"
```

---

## Task 4: `ThemeResolver` (pure, TDD)

**Files:**
- Create: `AdbUi.Theme/ThemeResolver.cs`
- Test: `AdbUi.Theme.Tests/ThemeResolverTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbUi.Theme.Tests/ThemeResolverTests.cs`:

```csharp
using AdbUi.Theme;

namespace AdbUi.Theme.Tests;

public class ThemeResolverTests
{
    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.HighContrast)]
    public void System_resolves_to_the_os_theme(AppTheme osTheme)
    {
        Assert.Equal(osTheme, ThemeResolver.Resolve(ThemeSelection.System, osTheme));
    }

    [Theory]
    [InlineData(ThemeSelection.Light, AppTheme.Light)]
    [InlineData(ThemeSelection.Dark, AppTheme.Dark)]
    [InlineData(ThemeSelection.HighContrast, AppTheme.HighContrast)]
    public void Explicit_selection_ignores_the_os_theme(ThemeSelection selection, AppTheme expected)
    {
        // OS theme is deliberately different from the selection to prove it is ignored.
        Assert.Equal(expected, ThemeResolver.Resolve(selection, AppTheme.Dark));
        Assert.Equal(expected, ThemeResolver.Resolve(selection, AppTheme.Light));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj`
Expected: FAIL — `ThemeResolver` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `AdbUi.Theme/ThemeResolver.cs`:

```csharp
namespace AdbUi.Theme;

/// <summary>Resolves a <see cref="ThemeSelection"/> (which may be "follow the OS") plus the current OS theme
/// into the single <see cref="AppTheme"/> that should be applied.</summary>
public static class ThemeResolver
{
    public static AppTheme Resolve(ThemeSelection selection, AppTheme osTheme) => selection switch
    {
        ThemeSelection.Light => AppTheme.Light,
        ThemeSelection.Dark => AppTheme.Dark,
        ThemeSelection.HighContrast => AppTheme.HighContrast,
        _ => osTheme, // ThemeSelection.System
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj`
Expected: PASS (6 test cases — two `[Theory]` blocks, 3 cases each).

- [ ] **Step 5: Commit**

```bash
git add AdbUi.Theme/ThemeResolver.cs AdbUi.Theme.Tests/ThemeResolverTests.cs
git commit -m "feat(theme): add ThemeResolver with tests"
```

---

## Task 5: `AppSettings` + `JsonSettingsStore` (TDD)

**Files:**
- Create: `AdbUi.Theme/AppSettings.cs`
- Create: `AdbUi.Theme/ISettingsStore.cs`
- Create: `AdbUi.Theme/JsonSettingsStore.cs`
- Test: `AdbUi.Theme.Tests/JsonSettingsStoreTests.cs`

- [ ] **Step 1: Create `AppSettings` and `ISettingsStore`**

Create `AdbUi.Theme/AppSettings.cs`:

```csharp
namespace AdbUi.Theme;

/// <summary>Persisted application settings. A general bag so future settings have a home; v1 only carries the
/// theme choice.</summary>
public sealed record AppSettings
{
    public ThemeSelection Theme { get; init; } = ThemeSelection.System;
}
```

Create `AdbUi.Theme/ISettingsStore.cs`:

```csharp
namespace AdbUi.Theme;

/// <summary>Loads and saves <see cref="AppSettings"/>. Implementations must never throw on a missing or corrupt
/// store — they return defaults instead.</summary>
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
```

- [ ] **Step 2: Write the failing tests**

Create `AdbUi.Theme.Tests/JsonSettingsStoreTests.cs`:

```csharp
using System.IO;
using AdbUi.Theme;

namespace AdbUi.Theme.Tests;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"adb-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Load_returns_defaults_when_file_is_missing()
    {
        var store = new JsonSettingsStore(_path);

        Assert.Equal(ThemeSelection.System, store.Load().Theme);
    }

    [Fact]
    public void Save_then_Load_round_trips_the_theme()
    {
        var store = new JsonSettingsStore(_path);

        store.Save(new AppSettings { Theme = ThemeSelection.Dark });

        Assert.Equal(ThemeSelection.Dark, store.Load().Theme);
    }

    [Fact]
    public void Load_returns_defaults_when_file_is_corrupt()
    {
        File.WriteAllText(_path, "{ this is not valid json");
        var store = new JsonSettingsStore(_path);

        Assert.Equal(ThemeSelection.System, store.Load().Theme);
    }

    [Fact]
    public void Save_after_corrupt_rewrites_valid_json()
    {
        File.WriteAllText(_path, "garbage");
        var store = new JsonSettingsStore(_path);

        store.Save(new AppSettings { Theme = ThemeSelection.HighContrast });

        Assert.Equal(ThemeSelection.HighContrast, new JsonSettingsStore(_path).Load().Theme);
    }

    [Fact]
    public void Save_creates_the_parent_directory_if_missing()
    {
        var nestedDir = Path.Combine(Path.GetTempPath(), $"adb-{Guid.NewGuid():N}");
        var nestedPath = Path.Combine(nestedDir, "settings.json");
        try
        {
            new JsonSettingsStore(nestedPath).Save(new AppSettings { Theme = ThemeSelection.Light });

            Assert.True(File.Exists(nestedPath));
        }
        finally
        {
            if (Directory.Exists(nestedDir)) Directory.Delete(nestedDir, recursive: true);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj`
Expected: FAIL — `JsonSettingsStore` does not exist.

- [ ] **Step 4: Write the implementation**

Create `AdbUi.Theme/JsonSettingsStore.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdbUi.Theme;

/// <summary>JSON-backed settings store. Stores enums by name (so the file is human-readable and stable across
/// enum-reordering). Resilient: a missing or unreadable file yields defaults rather than throwing.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public JsonSettingsStore(string path) => _path = path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj`
Expected: PASS (11 test cases total: 6 resolver + 5 settings).

- [ ] **Step 6: Commit**

```bash
git add AdbUi.Theme/AppSettings.cs AdbUi.Theme/ISettingsStore.cs AdbUi.Theme/JsonSettingsStore.cs AdbUi.Theme.Tests/JsonSettingsStoreTests.cs
git commit -m "feat(theme): add AppSettings and resilient JsonSettingsStore with tests"
```

---

## Task 6: `SettingsPaths`

**Files:**
- Create: `AdbUi.Theme/SettingsPaths.cs`

No unit test: this is a thin wrapper over `Environment.GetFolderPath` (environment-dependent, verified by use).

- [ ] **Step 1: Create `SettingsPaths`**

Create `AdbUi.Theme/SettingsPaths.cs`:

```csharp
using System.IO;

namespace AdbUi.Theme;

/// <summary>Resolves the on-disk location of the shared settings file: <c>%AppData%/ADB/settings.json</c>.
/// Both BotBuilder and BotCapture read/write this same file so they stay in sync on the chosen theme.</summary>
public static class SettingsPaths
{
    public static string SettingsFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADB",
            "settings.json");
}
```

- [ ] **Step 2: Build**

Run: `dotnet build AdbUi.Theme/AdbUi.Theme.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add AdbUi.Theme/SettingsPaths.cs
git commit -m "feat(theme): add SettingsPaths for the shared %AppData%/ADB/settings.json"
```

---

## Task 7: `IOsThemeProbe` + fake + `Win32OsThemeProbe`

**Files:**
- Create: `AdbUi.Theme/IOsThemeProbe.cs`
- Create: `AdbUi.Theme/Win32OsThemeProbe.cs`
- Create: `AdbUi.Theme.Tests/Fakes/FakeOsThemeProbe.cs`

The interface + fake are needed by Task 8's `ThemeManager` tests. The live Win32 implementation is verified during slices 2/3 (it reads the registry + listens to system events — environment-bound).

- [ ] **Step 1: Create the interface**

Create `AdbUi.Theme/IOsThemeProbe.cs`:

```csharp
namespace AdbUi.Theme;

/// <summary>Reports the current OS theme and raises an event when the user changes it. Used only while the
/// app's theme selection is <see cref="ThemeSelection.System"/>.</summary>
public interface IOsThemeProbe
{
    /// <summary>The OS's current effective theme.</summary>
    AppTheme Current { get; }

    /// <summary>Raised when the OS theme changes (e.g. the user toggles Windows dark mode).</summary>
    event EventHandler? OsThemeChanged;
}
```

- [ ] **Step 2: Create the fake (test double)**

Create `AdbUi.Theme.Tests/Fakes/FakeOsThemeProbe.cs`:

```csharp
using AdbUi.Theme;

namespace AdbUi.Theme.Tests.Fakes;

/// <summary>Test double for <see cref="IOsThemeProbe"/>. Set <see cref="Current"/> and call
/// <see cref="RaiseChanged"/> to simulate an OS theme change.</summary>
public sealed class FakeOsThemeProbe : IOsThemeProbe
{
    public AppTheme Current { get; set; } = AppTheme.Light;

    public event EventHandler? OsThemeChanged;

    public void RaiseChanged(AppTheme newTheme)
    {
        Current = newTheme;
        OsThemeChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 3: Create the live Win32 implementation**

Create `AdbUi.Theme/Win32OsThemeProbe.cs`:

```csharp
using Microsoft.Win32;
using System.Windows;

namespace AdbUi.Theme;

/// <summary>Live OS theme probe for Windows. Reads the user's app-theme preference from the registry and the
/// high-contrast flag from WPF system parameters, and re-raises <see cref="OsThemeChanged"/> when Windows
/// signals a user-preference change. Verified live (registry + SystemEvents are environment-bound).</summary>
public sealed class Win32OsThemeProbe : IOsThemeProbe
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    public Win32OsThemeProbe()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public AppTheme Current => Detect();

    public event EventHandler? OsThemeChanged;

    private static AppTheme Detect()
    {
        // High contrast wins over light/dark.
        if (SystemParameters.HighContrast) return AppTheme.HighContrast;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // AppsUseLightTheme: 1 = light, 0 = dark. Missing => assume light.
            var value = key?.GetValue(AppsUseLightThemeValue);
            if (value is int i) return i == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch
        {
            // Registry unreadable — fall through to the light default.
        }

        return AppTheme.Light;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // General + Color + Accessibility cover dark-mode toggles and high-contrast changes.
        if (e.Category is UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.Accessibility)
        {
            OsThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add AdbUi.Theme/IOsThemeProbe.cs AdbUi.Theme/Win32OsThemeProbe.cs AdbUi.Theme.Tests/Fakes/FakeOsThemeProbe.cs
git commit -m "feat(theme): add IOsThemeProbe with Win32 impl and test fake"
```

---

## Task 8: `IThemeApplier` + `ThemeManager` (TDD)

**Files:**
- Create: `AdbUi.Theme/IThemeApplier.cs`
- Create: `AdbUi.Theme/ThemeManager.cs`
- Create: `AdbUi.Theme.Tests/Fakes/FakeThemeApplier.cs`
- Create: `AdbUi.Theme.Tests/Fakes/FakeSettingsStore.cs`
- Test: `AdbUi.Theme.Tests/ThemeManagerTests.cs`

`ThemeManager` holds all the orchestration logic (resolve → apply → persist → subscribe-to-OS-while-System). The actual WPF resource swap is behind `IThemeApplier`, so the manager is fully unit-testable with fakes.

- [ ] **Step 1: Create `IThemeApplier`**

Create `AdbUi.Theme/IThemeApplier.cs`:

```csharp
namespace AdbUi.Theme;

/// <summary>Applies an effective <see cref="AppTheme"/> to the running UI (swaps the active brush dictionary).
/// Abstracted so <see cref="ThemeManager"/>'s logic is testable without a live WPF Application.</summary>
public interface IThemeApplier
{
    void Apply(AppTheme theme);
}
```

- [ ] **Step 2: Create the fakes**

Create `AdbUi.Theme.Tests/Fakes/FakeThemeApplier.cs`:

```csharp
using AdbUi.Theme;

namespace AdbUi.Theme.Tests.Fakes;

/// <summary>Records every theme it was asked to apply.</summary>
public sealed class FakeThemeApplier : IThemeApplier
{
    public List<AppTheme> Applied { get; } = new();

    public AppTheme? Last => Applied.Count == 0 ? null : Applied[^1];

    public void Apply(AppTheme theme) => Applied.Add(theme);
}
```

Create `AdbUi.Theme.Tests/Fakes/FakeSettingsStore.cs`:

```csharp
using AdbUi.Theme;

namespace AdbUi.Theme.Tests.Fakes;

/// <summary>In-memory settings store. Starts from <paramref name="initial"/>; records saves.</summary>
public sealed class FakeSettingsStore : ISettingsStore
{
    private AppSettings _settings;

    public FakeSettingsStore(AppSettings? initial = null) => _settings = initial ?? new AppSettings();

    public int SaveCount { get; private set; }

    public AppSettings Load() => _settings;

    public void Save(AppSettings settings)
    {
        _settings = settings;
        SaveCount++;
    }
}
```

- [ ] **Step 3: Write the failing tests**

Create `AdbUi.Theme.Tests/ThemeManagerTests.cs`:

```csharp
using AdbUi.Theme;
using AdbUi.Theme.Tests.Fakes;

namespace AdbUi.Theme.Tests;

public class ThemeManagerTests
{
    private static (ThemeManager mgr, FakeThemeApplier applier, FakeOsThemeProbe probe, FakeSettingsStore store)
        Build(ThemeSelection initial, AppTheme osTheme = AppTheme.Light)
    {
        var applier = new FakeThemeApplier();
        var probe = new FakeOsThemeProbe { Current = osTheme };
        var store = new FakeSettingsStore(new AppSettings { Theme = initial });
        var mgr = new ThemeManager(store, probe, applier);
        return (mgr, applier, probe, store);
    }

    [Fact]
    public void Initialize_applies_the_saved_explicit_theme()
    {
        var (mgr, applier, _, _) = Build(ThemeSelection.Dark);

        mgr.Initialize();

        Assert.Equal(AppTheme.Dark, applier.Last);
        Assert.Equal(ThemeSelection.Dark, mgr.CurrentSelection);
    }

    [Fact]
    public void Initialize_with_System_applies_the_os_theme()
    {
        var (mgr, applier, _, _) = Build(ThemeSelection.System, osTheme: AppTheme.Dark);

        mgr.Initialize();

        Assert.Equal(AppTheme.Dark, applier.Last);
    }

    [Fact]
    public void Apply_changes_theme_and_persists_the_selection()
    {
        var (mgr, applier, _, store) = Build(ThemeSelection.System, osTheme: AppTheme.Light);
        mgr.Initialize();

        mgr.Apply(ThemeSelection.HighContrast);

        Assert.Equal(AppTheme.HighContrast, applier.Last);
        Assert.Equal(ThemeSelection.HighContrast, mgr.CurrentSelection);
        Assert.Equal(ThemeSelection.HighContrast, store.Load().Theme);
    }

    [Fact]
    public void Os_change_reapplies_while_following_System()
    {
        var (mgr, applier, probe, _) = Build(ThemeSelection.System, osTheme: AppTheme.Light);
        mgr.Initialize();

        probe.RaiseChanged(AppTheme.Dark);

        Assert.Equal(AppTheme.Dark, applier.Last);
    }

    [Fact]
    public void Os_change_is_ignored_when_an_explicit_theme_is_selected()
    {
        var (mgr, applier, probe, _) = Build(ThemeSelection.Light, osTheme: AppTheme.Light);
        mgr.Initialize();
        var appliedCountAfterInit = applier.Applied.Count;

        probe.RaiseChanged(AppTheme.Dark);

        Assert.Equal(appliedCountAfterInit, applier.Applied.Count); // no extra apply
        Assert.Equal(AppTheme.Light, applier.Last);
    }

    [Fact]
    public void Switching_from_System_to_explicit_then_os_change_does_nothing()
    {
        var (mgr, applier, probe, _) = Build(ThemeSelection.System, osTheme: AppTheme.Light);
        mgr.Initialize();
        mgr.Apply(ThemeSelection.Dark); // leave System

        var count = applier.Applied.Count;
        probe.RaiseChanged(AppTheme.Light);

        Assert.Equal(count, applier.Applied.Count);
    }

    [Fact]
    public void SelectionChanged_fires_on_apply()
    {
        var (mgr, _, _, _) = Build(ThemeSelection.System);
        mgr.Initialize();
        ThemeSelection? observed = null;
        mgr.SelectionChanged += (_, _) => observed = mgr.CurrentSelection;

        mgr.Apply(ThemeSelection.Dark);

        Assert.Equal(ThemeSelection.Dark, observed);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj`
Expected: FAIL — `ThemeManager` does not exist.

- [ ] **Step 5: Write the implementation**

Create `AdbUi.Theme/ThemeManager.cs`:

```csharp
namespace AdbUi.Theme;

/// <summary>Owns the app's current theme selection. Resolves it (consulting the OS when "System"), applies the
/// effective theme via <see cref="IThemeApplier"/>, persists the choice, and re-applies on OS theme changes
/// while the selection follows the OS.</summary>
public sealed class ThemeManager
{
    private readonly ISettingsStore _store;
    private readonly IOsThemeProbe _osProbe;
    private readonly IThemeApplier _applier;

    public ThemeManager(ISettingsStore store, IOsThemeProbe osProbe, IThemeApplier applier)
    {
        _store = store;
        _osProbe = osProbe;
        _applier = applier;
        _osProbe.OsThemeChanged += OnOsThemeChanged;
    }

    /// <summary>The user's current theme choice.</summary>
    public ThemeSelection CurrentSelection { get; private set; } = ThemeSelection.System;

    /// <summary>Raised after <see cref="CurrentSelection"/> changes (so menus can update their checkmarks).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Loads the persisted selection and applies the resulting theme. Call once at app startup.</summary>
    public void Initialize()
    {
        CurrentSelection = _store.Load().Theme;
        ApplyEffective();
    }

    /// <summary>Switches to <paramref name="selection"/>, applies it, and persists the choice.</summary>
    public void Apply(ThemeSelection selection)
    {
        CurrentSelection = selection;
        ApplyEffective();
        _store.Save(new AppSettings { Theme = selection });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyEffective() =>
        _applier.Apply(ThemeResolver.Resolve(CurrentSelection, _osProbe.Current));

    private void OnOsThemeChanged(object? sender, EventArgs e)
    {
        // Only honor OS changes while we are following the OS.
        if (CurrentSelection == ThemeSelection.System) ApplyEffective();
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test AdbUi.Theme.Tests/AdbUi.Theme.Tests.csproj`
Expected: PASS (18 test cases total: 6 resolver + 5 settings + 7 manager).

- [ ] **Step 7: Commit**

```bash
git add AdbUi.Theme/IThemeApplier.cs AdbUi.Theme/ThemeManager.cs AdbUi.Theme.Tests/Fakes/FakeThemeApplier.cs AdbUi.Theme.Tests/Fakes/FakeSettingsStore.cs AdbUi.Theme.Tests/ThemeManagerTests.cs
git commit -m "feat(theme): add ThemeManager (resolve/apply/persist/OS-follow) with tests"
```

---

## Task 9: Brush dictionaries (Light / Dark / High-Contrast)

**Files:**
- Create: `AdbUi.Theme/Themes/Light.xaml`
- Create: `AdbUi.Theme/Themes/Dark.xaml`
- Create: `AdbUi.Theme/Themes/HighContrast.xaml`

Each dictionary defines the **identical** set of `SolidColorBrush` keys (the §4 contract). Colours below are sensible starting values; they are tuned during the visual-verification of slices 2 & 3. Keep the key list identical across all three files — a missing key in one theme is a runtime `XamlParseException` for any consumer using it.

- [ ] **Step 1: Create `Light.xaml`**

Create `AdbUi.Theme/Themes/Light.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="PanelBackgroundBrush" Color="#F7F7F7" />
    <SolidColorBrush x:Key="SurfaceBackgroundBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="CanvasBackgroundBrush" Color="#FAFAFA" />
    <SolidColorBrush x:Key="PrimaryTextBrush" Color="#1E1E1E" />
    <SolidColorBrush x:Key="SecondaryTextBrush" Color="#666666" />
    <SolidColorBrush x:Key="DisabledTextBrush" Color="#AAAAAA" />
    <SolidColorBrush x:Key="BorderBrush" Color="#DDDDDD" />
    <SolidColorBrush x:Key="AccentBrush" Color="#4A90D9" />
    <SolidColorBrush x:Key="AccentTextBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="ControlBackgroundBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="ControlBorderBrush" Color="#CCCCCC" />
    <SolidColorBrush x:Key="ControlHoverBackgroundBrush" Color="#ECECEC" />
    <SolidColorBrush x:Key="MenuBackgroundBrush" Color="#F0F0F0" />
    <SolidColorBrush x:Key="ScrollBarThumbBrush" Color="#C0C0C0" />
    <SolidColorBrush x:Key="SelectionBackgroundBrush" Color="#CCE4F7" />
    <SolidColorBrush x:Key="ErrorBrush" Color="#D0021B" />
    <SolidColorBrush x:Key="SuccessBrush" Color="#2E7D32" />

</ResourceDictionary>
```

- [ ] **Step 2: Create `Dark.xaml`**

Create `AdbUi.Theme/Themes/Dark.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="#1E1E1E" />
    <SolidColorBrush x:Key="PanelBackgroundBrush" Color="#252526" />
    <SolidColorBrush x:Key="SurfaceBackgroundBrush" Color="#2D2D30" />
    <SolidColorBrush x:Key="CanvasBackgroundBrush" Color="#1B1B1C" />
    <SolidColorBrush x:Key="PrimaryTextBrush" Color="#E8E8E8" />
    <SolidColorBrush x:Key="SecondaryTextBrush" Color="#A0A0A0" />
    <SolidColorBrush x:Key="DisabledTextBrush" Color="#6A6A6A" />
    <SolidColorBrush x:Key="BorderBrush" Color="#3F3F46" />
    <SolidColorBrush x:Key="AccentBrush" Color="#4A90D9" />
    <SolidColorBrush x:Key="AccentTextBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="ControlBackgroundBrush" Color="#333337" />
    <SolidColorBrush x:Key="ControlBorderBrush" Color="#555555" />
    <SolidColorBrush x:Key="ControlHoverBackgroundBrush" Color="#3E3E42" />
    <SolidColorBrush x:Key="MenuBackgroundBrush" Color="#2D2D30" />
    <SolidColorBrush x:Key="ScrollBarThumbBrush" Color="#555555" />
    <SolidColorBrush x:Key="SelectionBackgroundBrush" Color="#094771" />
    <SolidColorBrush x:Key="ErrorBrush" Color="#F48771" />
    <SolidColorBrush x:Key="SuccessBrush" Color="#6A9955" />

</ResourceDictionary>
```

- [ ] **Step 3: Create `HighContrast.xaml`**

Create `AdbUi.Theme/Themes/HighContrast.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="#000000" />
    <SolidColorBrush x:Key="PanelBackgroundBrush" Color="#000000" />
    <SolidColorBrush x:Key="SurfaceBackgroundBrush" Color="#000000" />
    <SolidColorBrush x:Key="CanvasBackgroundBrush" Color="#000000" />
    <SolidColorBrush x:Key="PrimaryTextBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="SecondaryTextBrush" Color="#FFFF00" />
    <SolidColorBrush x:Key="DisabledTextBrush" Color="#A6A6A6" />
    <SolidColorBrush x:Key="BorderBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="AccentBrush" Color="#1AEBFF" />
    <SolidColorBrush x:Key="AccentTextBrush" Color="#000000" />
    <SolidColorBrush x:Key="ControlBackgroundBrush" Color="#000000" />
    <SolidColorBrush x:Key="ControlBorderBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="ControlHoverBackgroundBrush" Color="#1AEBFF" />
    <SolidColorBrush x:Key="MenuBackgroundBrush" Color="#000000" />
    <SolidColorBrush x:Key="ScrollBarThumbBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="SelectionBackgroundBrush" Color="#1AEBFF" />
    <SolidColorBrush x:Key="ErrorBrush" Color="#FF6B6B" />
    <SolidColorBrush x:Key="SuccessBrush" Color="#00FF00" />

</ResourceDictionary>
```

- [ ] **Step 4: Build to verify the XAML parses**

Run: `dotnet build AdbUi.Theme/AdbUi.Theme.csproj`
Expected: Build succeeded (the dictionaries compile into the assembly as resources).

- [ ] **Step 5: Commit**

```bash
git add AdbUi.Theme/Themes/Light.xaml AdbUi.Theme/Themes/Dark.xaml AdbUi.Theme/Themes/HighContrast.xaml
git commit -m "feat(theme): add Light/Dark/HighContrast brush dictionaries"
```

---

## Task 10: `Controls.xaml` — theme-agnostic implicit control styles

**Files:**
- Create: `AdbUi.Theme/Themes/Controls.xaml`

Implicit (keyless `TargetType`) styles for the standard controls the apps use, every colour via `{DynamicResource}` so they recolour with the active theme. This is merged once at startup (Task: app wiring, slice 2) alongside the active brush dictionary.

- [ ] **Step 1: Create `Controls.xaml`**

Create `AdbUi.Theme/Themes/Controls.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Base text -->
    <Style TargetType="TextBlock">
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
    </Style>

    <Style TargetType="Label">
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
    </Style>

    <!-- TextBox -->
    <Style TargetType="TextBox">
        <Setter Property="Background" Value="{DynamicResource ControlBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource ControlBorderBrush}" />
        <Setter Property="CaretBrush" Value="{DynamicResource PrimaryTextBrush}" />
    </Style>

    <!-- Button -->
    <Style TargetType="Button">
        <Setter Property="Background" Value="{DynamicResource ControlBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource ControlBorderBrush}" />
        <Setter Property="Padding" Value="8,3" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="Bd"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="1" CornerRadius="3"
                            Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background"
                                    Value="{DynamicResource ControlHoverBackgroundBrush}" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Foreground" Value="{DynamicResource DisabledTextBrush}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Menu / MenuItem -->
    <Style TargetType="Menu">
        <Setter Property="Background" Value="{DynamicResource PanelBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
    </Style>

    <Style TargetType="MenuItem">
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
    </Style>

    <Style TargetType="ContextMenu">
        <Setter Property="Background" Value="{DynamicResource MenuBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
    </Style>

    <Style TargetType="Separator">
        <Setter Property="Background" Value="{DynamicResource BorderBrush}" />
    </Style>

    <!-- ComboBox -->
    <Style TargetType="ComboBox">
        <Setter Property="Background" Value="{DynamicResource ControlBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource ControlBorderBrush}" />
    </Style>

    <Style TargetType="ComboBoxItem">
        <Setter Property="Background" Value="{DynamicResource ControlBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
    </Style>

    <!-- ToolTip -->
    <Style TargetType="ToolTip">
        <Setter Property="Background" Value="{DynamicResource MenuBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
    </Style>

</ResourceDictionary>
```

- [ ] **Step 2: Build to verify the XAML parses**

Run: `dotnet build AdbUi.Theme/AdbUi.Theme.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add AdbUi.Theme/Themes/Controls.xaml
git commit -m "feat(theme): add theme-agnostic implicit control styles (Controls.xaml)"
```

---

## Task 11: `ResourceDictionaryThemeApplier` (live WPF applier)

**Files:**
- Create: `AdbUi.Theme/ResourceDictionaryThemeApplier.cs`

The live `IThemeApplier`. Swaps exactly one tagged theme-brush dictionary in `Application.Current.Resources.MergedDictionaries` so DynamicResource bindings update and no duplicates accumulate. Verified live in slices 2/3.

- [ ] **Step 1: Create the applier**

Create `AdbUi.Theme/ResourceDictionaryThemeApplier.cs`:

```csharp
using System.Windows;

namespace AdbUi.Theme;

/// <summary>Live <see cref="IThemeApplier"/>: merges the active theme's brush dictionary into the WPF
/// application resources, removing any previously-applied theme dictionary so exactly one is ever present.
/// The shared <c>Controls.xaml</c> is merged once by the app at startup and is never swapped.</summary>
public sealed class ResourceDictionaryThemeApplier : IThemeApplier
{
    private const string AssemblyName = "AdbUi.Theme";
    private ResourceDictionary? _current;

    public void Apply(AppTheme theme)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("No WPF Application is running.");

        var next = new ResourceDictionary { Source = ThemeUri(theme) };

        var merged = app.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(next);
        _current = next;
    }

    private static Uri ThemeUri(AppTheme theme)
    {
        var file = theme switch
        {
            AppTheme.Dark => "Dark",
            AppTheme.HighContrast => "HighContrast",
            _ => "Light",
        };
        return new Uri($"pack://application:,,,/{AssemblyName};component/Themes/{file}.xaml", UriKind.Absolute);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build AdbUi.Theme/AdbUi.Theme.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add AdbUi.Theme/ResourceDictionaryThemeApplier.cs
git commit -m "feat(theme): add live ResourceDictionaryThemeApplier (tagged single-swap)"
```

---

## Task 12: Full-solution build + test gate

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build ADB.slnx`
Expected: Build succeeded, 0 warnings, 0 errors (the new projects compile alongside the existing ones; nothing consumes `AdbUi.Theme` yet, so no existing project changes).

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test ADB.slnx`
Expected: All tests pass, including the 18 new `AdbUi.Theme.Tests` cases. No regressions in the existing suites.

- [ ] **Step 3: Commit (only if any fixup was needed; otherwise skip)**

```bash
git add -A
git commit -m "build(theme): green full-solution build + tests for theme core slice"
```

---

## Done — Slice 1 complete

At this point `AdbUi.Theme` exists as a fully unit-tested foundation: enums, `ThemeResolver`, `AppSettings`/`JsonSettingsStore`, `SettingsPaths`, `IOsThemeProbe`/`Win32OsThemeProbe`, `IThemeApplier`/`ResourceDictionaryThemeApplier`, `ThemeManager`, three brush dictionaries, and `Controls.xaml`. Nothing consumes it yet, so there is no visual change to either app — this slice can be self-merged as a backend foundation.

**Next:** Slice 2 (BotBuilder adoption) gets its own plan: reference `AdbUi.Theme`, wire `ThemeManager.Initialize` at startup with the live applier + Win32 probe + `JsonSettingsStore(SettingsPaths.SettingsFile)`, add the `View ▸ Theme` menu, and migrate BotBuilder's 5 themed XAML files from inline hex to `{DynamicResource}` brush keys. That slice is visually verified by the user.
