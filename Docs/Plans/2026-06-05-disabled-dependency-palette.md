# Disabled-Dependency Palette Greying — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`). Logic tasks are TDD; the XAML task has no unit test (verify = clean build + human visual check). Full solution test suite must stay green.

**Goal:** Grey out the Android and Browser palette categories (header + items) with an explanatory tooltip when their tooling isn't present (`adb` on PATH / Playwright browsers installed), while keeping the items draggable (soft-advisory model).

**Architecture:** A testable `IDependencyProbe` in `BotBuilder.Core` reports per-category availability; `PaletteCategory`/`PaletteItem` carry `IsAvailable`/`DisabledReason`; `PaletteViewModel` stamps them via the probe; the BotBuilder palette XAML greys + tooltips via `DataTrigger` on `IsAvailable` and `ToolTip` on `DisabledReason`. Greyed items use the themed `DisabledTextBrush`.

**Tech Stack:** .NET `net10.0-windows`, WPF, xUnit. Builds on the merged theming (`DisabledTextBrush`).

**Spec:** `Docs/Specs/2026-06-05-disabled-dependency-palette-design.md`

---

## Task 1: `IDependencyProbe` + `DependencyProbe` (TDD)

**Files:**
- Create: `BotBuilder.Core/Palette/DependencyProbe.cs`
- Test: `BotBuilder.Core.Tests/DependencyProbeTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/DependencyProbeTests.cs`:

```csharp
using BotBuilder.Core.Palette;
using Xunit;

namespace BotBuilder.Core.Tests;

public class DependencyProbeTests
{
    [Fact]
    public void Android_available_when_check_passes()
    {
        var probe = new DependencyProbe(androidAvailable: () => true, browserAvailable: () => false);

        var status = probe.ForCategory("Android");

        Assert.True(status.IsAvailable);
        Assert.Null(status.Reason);
    }

    [Fact]
    public void Android_unavailable_reports_path_reason()
    {
        var probe = new DependencyProbe(androidAvailable: () => false, browserAvailable: () => true);

        var status = probe.ForCategory("Android");

        Assert.False(status.IsAvailable);
        Assert.Equal("adb not found on PATH", status.Reason);
    }

    [Fact]
    public void Browser_available_when_check_passes()
    {
        var probe = new DependencyProbe(androidAvailable: () => false, browserAvailable: () => true);

        Assert.True(probe.ForCategory("Browser").IsAvailable);
    }

    [Fact]
    public void Browser_unavailable_reports_install_reason()
    {
        var probe = new DependencyProbe(androidAvailable: () => true, browserAvailable: () => false);

        var status = probe.ForCategory("Browser");

        Assert.False(status.IsAvailable);
        Assert.Equal("No browser engine found — run 'playwright install'", status.Reason);
    }

    [Theory]
    [InlineData("Screen")]
    [InlineData("Input")]
    [InlineData("Scripting")]
    [InlineData("Control Flow")]
    public void Other_categories_are_always_available(string category)
    {
        var probe = new DependencyProbe(androidAvailable: () => false, browserAvailable: () => false);

        var status = probe.ForCategory(category);

        Assert.True(status.IsAvailable);
        Assert.Null(status.Reason);
    }
}
```

- [ ] **Step 2: Run tests — confirm they FAIL to compile** (`DependencyProbe` doesn't exist):
`dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj`

- [ ] **Step 3: Implement.** Create `BotBuilder.Core/Palette/DependencyProbe.cs`:

```csharp
using System.IO;

namespace BotBuilder.Core.Palette;

/// <summary>Availability of the tooling a palette category needs, plus a human reason when it is missing.</summary>
public sealed record DependencyStatus(bool IsAvailable, string? Reason)
{
    public static readonly DependencyStatus Available = new(true, null);
}

/// <summary>Reports whether a palette category's external dependency is present on this machine.</summary>
public interface IDependencyProbe
{
    DependencyStatus ForCategory(string category);
}

/// <summary>Live dependency probe. Android needs <c>adb</c> on the PATH; Browser needs at least one Playwright
/// browser engine in the Playwright cache. The environment checks are injectable so the category→status
/// mapping is unit-testable without touching the real PATH/filesystem.</summary>
public sealed class DependencyProbe : IDependencyProbe
{
    private readonly Func<bool> _androidAvailable;
    private readonly Func<bool> _browserAvailable;

    public DependencyProbe(Func<bool>? androidAvailable = null, Func<bool>? browserAvailable = null)
    {
        _androidAvailable = androidAvailable ?? DefaultAndroidCheck;
        _browserAvailable = browserAvailable ?? DefaultBrowserCheck;
    }

    public DependencyStatus ForCategory(string category) => category switch
    {
        "Android" => _androidAvailable()
            ? DependencyStatus.Available
            : new DependencyStatus(false, "adb not found on PATH"),
        "Browser" => _browserAvailable()
            ? DependencyStatus.Available
            : new DependencyStatus(false, "No browser engine found — run 'playwright install'"),
        _ => DependencyStatus.Available,
    };

    // adb resolvable on the PATH (how the runtime locates the ADB server).
    private static bool DefaultAndroidCheck()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            try
            {
                if (File.Exists(Path.Combine(dir.Trim(), "adb.exe")))
                {
                    return true;
                }
            }
            catch
            {
                // Malformed PATH entry — skip it.
            }
        }

        return false;
    }

    // At least one Playwright browser engine present in the Playwright browsers cache.
    private static bool DefaultBrowserCheck()
    {
        try
        {
            var root = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ms-playwright");
            }

            if (!Directory.Exists(root))
            {
                return false;
            }

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith("chromium", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("firefox", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("webkit", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Defensive: any IO failure means we can't confirm browsers → treat as unavailable.
        }

        return false;
    }
}
```

- [ ] **Step 4: Run tests — confirm PASS** (8 cases: 4 facts + a 4-case theory): `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj`

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Palette/DependencyProbe.cs BotBuilder.Core.Tests/DependencyProbeTests.cs
git commit -m "feat(palette): add IDependencyProbe + DependencyProbe (adb/Playwright detection)"
```

---

## Task 2: Carry availability on palette VMs (TDD)

**Files:**
- Modify: `BotBuilder.Core/Palette/PaletteCategory.cs`
- Modify: `BotBuilder.Core/Palette/PaletteItem.cs`
- Modify: `BotBuilder.Core/Palette/PaletteViewModel.cs`
- Test: `BotBuilder.Core.Tests/PaletteViewModelTests.cs` (add to the existing file)

- [ ] **Step 1: Add the failing test**

Add to `BotBuilder.Core.Tests/PaletteViewModelTests.cs` — a fake probe (place near the top of the class, after `SeededRegistry`) and a test (anywhere in the class):

```csharp
    private sealed class FakeProbe : IDependencyProbe
    {
        private readonly HashSet<string> _unavailable;
        public FakeProbe(params string[] unavailable) => _unavailable = new HashSet<string>(unavailable);

        public DependencyStatus ForCategory(string category) =>
            _unavailable.Contains(category) ? new DependencyStatus(false, category + " missing") : DependencyStatus.Available;
    }

    [Fact]
    public void Unavailable_category_marks_its_items_and_leaves_others_available()
    {
        var palette = new PaletteViewModel(SeededRegistry(), new FakeProbe("Android"));

        var android = palette.Categories.Single(c => c.Name == "Android");
        Assert.False(android.IsAvailable);
        Assert.Equal("Android missing", android.DisabledReason);
        Assert.All(android.Items, i =>
        {
            Assert.False(i.IsAvailable);
            Assert.Equal("Android missing", i.DisabledReason);
        });

        var screen = palette.Categories.Single(c => c.Name == "Screen");
        Assert.True(screen.IsAvailable);
        Assert.Null(screen.DisabledReason);
        Assert.All(screen.Items, i => Assert.True(i.IsAvailable));
    }
```

- [ ] **Step 2: Run — confirm it FAILS to compile** (`IsAvailable`/two-arg ctor don't exist):
`dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj`

- [ ] **Step 3: Add the properties + propagation.**

Replace `BotBuilder.Core/Palette/PaletteCategory.cs` with:

```csharp
namespace BotBuilder.Core.Palette;

/// <summary>A named group of palette items, plus whether its external dependency is available here.</summary>
public sealed class PaletteCategory
{
    public PaletteCategory(string name, IReadOnlyList<PaletteItem> items, bool isAvailable = true, string? disabledReason = null)
    {
        Name = name;
        Items = items;
        IsAvailable = isAvailable;
        DisabledReason = disabledReason;
    }

    public string Name { get; }
    public IReadOnlyList<PaletteItem> Items { get; }

    /// <summary>False when this category's dependency (e.g. adb / Playwright) is missing on this machine.</summary>
    public bool IsAvailable { get; }

    /// <summary>Human explanation shown as a tooltip when <see cref="IsAvailable"/> is false; otherwise null.</summary>
    public string? DisabledReason { get; }
}
```

Replace `BotBuilder.Core/Palette/PaletteItem.cs` with:

```csharp
namespace BotBuilder.Core.Palette;

/// <summary>One draggable entry in the action palette. Carries its category's availability so the item template
/// can grey + tooltip directly.</summary>
public sealed class PaletteItem
{
    public PaletteItem(string typeKey, string displayName, string category, bool isAvailable = true, string? disabledReason = null)
    {
        TypeKey = typeKey;
        DisplayName = displayName;
        Category = category;
        IsAvailable = isAvailable;
        DisabledReason = disabledReason;
    }

    public string TypeKey { get; }
    public string DisplayName { get; }
    public string Category { get; }

    /// <summary>False when this item's category dependency is missing on this machine.</summary>
    public bool IsAvailable { get; }

    /// <summary>Human explanation shown as a tooltip when <see cref="IsAvailable"/> is false; otherwise null.</summary>
    public string? DisabledReason { get; }
}
```

In `BotBuilder.Core/Palette/PaletteViewModel.cs`: add an `IDependencyProbe` field + optional ctor param, and stamp status in `Rebuild`. Change the constructor and `Rebuild` to:

```csharp
    private readonly ActionRegistry _registry;
    private readonly IDependencyProbe _probe;

    [ObservableProperty] private string _searchText = string.Empty;

    public PaletteViewModel(ActionRegistry registry, IDependencyProbe? probe = null)
    {
        _registry = registry;
        _probe = probe ?? new DependencyProbe();
        Categories = new System.Collections.ObjectModel.ObservableCollection<PaletteCategory>();
        Rebuild();
    }
```

and in `Rebuild`, replace the `foreach` body that builds items/categories with:

```csharp
        foreach (var group in matches.GroupBy(d => d.Category).OrderBy(g => g.Key))
        {
            var status = _probe.ForCategory(group.Key);
            var items = group
                .OrderBy(d => d.DisplayName)
                .Select(d => new PaletteItem(d.TypeKey, d.DisplayName, d.Category, status.IsAvailable, status.Reason))
                .ToList();
            Categories.Add(new PaletteCategory(group.Key, items, status.IsAvailable, status.Reason));
        }
```

- [ ] **Step 4: Run — confirm PASS** (the new test + all existing palette tests): `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj`

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Palette/PaletteCategory.cs BotBuilder.Core/Palette/PaletteItem.cs BotBuilder.Core/Palette/PaletteViewModel.cs BotBuilder.Core.Tests/PaletteViewModelTests.cs
git commit -m "feat(palette): carry per-category dependency availability on palette VMs"
```

---

## Task 3: Grey + tooltip the palette in BotBuilder XAML

**Files:**
- Modify: `BotBuilder/MainWindow.xaml`

The palette templates are inside the `<DockPanel Grid.Column="0" ...>` (around lines 158–172). Read the file to locate them.

- [ ] **Step 1: Grey + tooltip the category header**

Replace the category-header line:
```xml
                                <TextBlock Text="{Binding Name}" FontWeight="Bold" Margin="0,2" />
```
with:
```xml
                                <TextBlock Text="{Binding Name}" FontWeight="Bold" Margin="0,2"
                                           ToolTip="{Binding DisabledReason}">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding IsAvailable}" Value="False">
                                                    <Setter Property="Foreground" Value="{DynamicResource DisabledTextBrush}" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
```

- [ ] **Step 2: Tooltip the item border + grey the item text**

Replace the item template Border + TextBlock:
```xml
                                            <Border Background="{DynamicResource SurfaceBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1"
                                                    CornerRadius="3" Padding="6,3" Margin="4,2"
                                                    MouseMove="PaletteItem_MouseMove"
                                                    MouseLeftButtonDown="PaletteItem_MouseLeftButtonDown">
                                                <TextBlock Text="{Binding DisplayName}" />
                                            </Border>
```
with:
```xml
                                            <Border Background="{DynamicResource SurfaceBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1"
                                                    CornerRadius="3" Padding="6,3" Margin="4,2"
                                                    ToolTip="{Binding DisabledReason}"
                                                    MouseMove="PaletteItem_MouseMove"
                                                    MouseLeftButtonDown="PaletteItem_MouseLeftButtonDown">
                                                <TextBlock Text="{Binding DisplayName}">
                                                    <TextBlock.Style>
                                                        <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding IsAvailable}" Value="False">
                                                                    <Setter Property="Foreground" Value="{DynamicResource DisabledTextBrush}" />
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </TextBlock.Style>
                                                </TextBlock>
                                            </Border>
```

Notes: `BasedOn="{StaticResource {x:Type TextBlock}}"` inherits the themed default foreground (`PrimaryTextBrush`) for the available case, so only the unavailable case overrides to `DisabledTextBrush`. A `ToolTip` bound to a null `DisabledReason` shows no tooltip (available items). The drag handlers are unchanged, so greyed items still add nodes (soft model).

- [ ] **Step 3: Build**

Run: `dotnet build BotBuilder/BotBuilder.csproj -warnaserror`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add BotBuilder/MainWindow.xaml
git commit -m "feat(palette): grey + tooltip palette categories whose deps are unavailable"
```

---

## Task 4: Full build + test gate + human visual verification

**Files:** none.

- [ ] **Step 1:** `dotnet build ADB.slnx -warnaserror` → 0 warnings, 0 errors.
- [ ] **Step 2:** `dotnet test ADB.slnx` → all pass (702: prior 693 + 9 new), no regressions.
- [ ] **Step 3: Hand off for visual verification.** Checklist for the user:
  - On a machine WITHOUT `adb` on PATH: the **Android** category header + its items render greyed; hovering shows "adb not found on PATH"; the items still drag onto the canvas.
  - Without Playwright browsers installed: the **Browser** category greys with "No browser engine found — run 'playwright install'".
  - Categories whose deps ARE present (Screen, Input, Data, Scripting, Control Flow) look normal.
  - Greying reads correctly in Light, Dark, and High-Contrast (uses `DisabledTextBrush`).

---

## Done

After the user verifies, this is a PR for the user to merge (it has a visual surface). The disabled-dependency palette UX — the original M9 request — is then complete.
