# M3c-2 — Target Bar + Target Assignment + Badges Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Target Bar (manage a bot's named targets), the tested target-assignment + node-badge logic, and `.bot` persistence of targets — completing milestone M3.

**Architecture:** Tested, WPF-free logic in `BotBuilder.Core`: a `TargetViewModel`/`TargetBarViewModel` for the target list, a `TargetId` on `NodeViewModel`, and editor logic that resolves each node's target-name badge (shown only when a bot has >1 target). The WPF shell replaces the Target Bar placeholder with editable target chips + "Add Target". The full per-node assignment UI is the Properties Panel's target dropdown in M4; M3c-2 ships the assignment *logic* (`AssignTarget`) tested, with badges defaulting unassigned nodes to the first target. Per `Docs/Specs/2026-06-01-m3-builder-canvas-design.md` (§5.1 Target Bar, §4.1, M3c slice).

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm, xUnit. Builds on merged M3c-1.

---

## Verification model
- **Tasks 1–3 (`BotBuilder.Core`)**: strict TDD via `dotnet test`.
- **Task 4 (`BotBuilder` WPF)**: `dotnet build ADB.slnx` 0 warnings + `dotnet test` green; Manual Verification Checklist.

## File Structure
```
BotBuilder.Core/
  Targets/
    TargetViewModel.cs        # NEW: wraps a BotTarget (Name/Type/Selector), AllTypes for UI
    TargetBarViewModel.cs     # NEW: Targets collection, AddTarget/RemoveTarget, Changed event
  NodeViewModel.cs            # MODIFIED: add observable TargetId
  BotEditorViewModel.cs       # MODIFIED: TargetBar property, AssignTarget, RefreshTargetBadges, New clears targets
  DocumentMapper.cs           # MODIFIED: round-trip Targets + node TargetId
BotBuilder/
  MainWindow.xaml             # MODIFIED: Target Bar chips replace the placeholder
  MainWindow.xaml.cs          # MODIFIED: Add/Remove target click handlers
BotBuilder.Core.Tests/
  TargetBarViewModelTests.cs, EditorTargetTests.cs, DocumentMapperTargetTests.cs
```

---

### Task 1: `TargetViewModel` + `TargetBarViewModel`

**Files:**
- Create: `BotBuilder.Core/Targets/TargetViewModel.cs`, `BotBuilder.Core/Targets/TargetBarViewModel.cs`
- Test: `BotBuilder.Core.Tests/TargetBarViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/TargetBarViewModelTests.cs`:
```csharp
using AdbCore.Models;
using BotBuilder.Core.Targets;
using Xunit;

namespace BotBuilder.Core.Tests;

public class TargetBarViewModelTests
{
    [Fact]
    public void AddTarget_AppendsWithDefaultsAndUniqueId()
    {
        var bar = new TargetBarViewModel();

        var a = bar.AddTarget();
        var b = bar.AddTarget();

        Assert.Equal(2, bar.Targets.Count);
        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(a.Id, b.Id);
        Assert.False(string.IsNullOrWhiteSpace(a.Name));
    }

    [Fact]
    public void RemoveTarget_RemovesIt()
    {
        var bar = new TargetBarViewModel();
        var a = bar.AddTarget();

        bar.RemoveTarget(a);

        Assert.Empty(bar.Targets);
    }

    [Fact]
    public void Changed_FiresOnAddAndRemove()
    {
        var bar = new TargetBarViewModel();
        var count = 0;
        bar.Changed += (_, _) => count++;

        var a = bar.AddTarget();
        bar.RemoveTarget(a);

        Assert.True(count >= 2);
    }

    [Fact]
    public void Changed_FiresWhenATargetIsRenamed()
    {
        var bar = new TargetBarViewModel();
        var a = bar.AddTarget();
        var fired = false;
        bar.Changed += (_, _) => fired = true;

        a.Name = "Renamed";

        Assert.True(fired);
    }

    [Fact]
    public void AllTypes_ExposesEveryTargetType()
    {
        Assert.Contains(BotTargetType.Window, TargetViewModel.AllTypes);
        Assert.Contains(BotTargetType.AndroidDevice, TargetViewModel.AllTypes);
        Assert.Contains(BotTargetType.Browser, TargetViewModel.AllTypes);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `TargetViewModel`, `TargetBarViewModel` don't exist.

- [ ] **Step 3: Implement the target view-models**

Create `BotBuilder.Core/Targets/TargetViewModel.cs`:
```csharp
using AdbCore.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Targets;

/// <summary>A single bot target (window / Android device / browser) shown as a chip in the Target Bar.</summary>
public partial class TargetViewModel : ObservableObject
{
    /// <summary>All target types, for binding a type picker in the UI.</summary>
    public static IReadOnlyList<BotTargetType> AllTypes { get; } = Enum.GetValues<BotTargetType>();

    public Guid Id { get; set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private BotTargetType _type;
    [ObservableProperty] private string _selector = string.Empty;
}
```

Create `BotBuilder.Core/Targets/TargetBarViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using AdbCore.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Targets;

/// <summary>The bar of bot targets. Raises <see cref="Changed"/> whenever the set of targets, or any
/// target's properties, change (so node badges can be refreshed).</summary>
public partial class TargetBarViewModel : ObservableObject
{
    public TargetBarViewModel()
    {
        Targets = new ObservableCollection<TargetViewModel>();
        Targets.CollectionChanged += OnCollectionChanged;
    }

    public ObservableCollection<TargetViewModel> Targets { get; }

    /// <summary>Raised when targets are added/removed or a target property changes.</summary>
    public event EventHandler? Changed;

    public TargetViewModel AddTarget()
    {
        var target = new TargetViewModel
        {
            Id = Guid.NewGuid(),
            Name = $"Target {Targets.Count + 1}",
            Type = BotTargetType.Window,
            Selector = string.Empty,
        };
        Targets.Add(target);
        return target;
    }

    public void RemoveTarget(TargetViewModel target) => Targets.Remove(target);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TargetViewModel t in e.OldItems) { t.PropertyChanged -= OnTargetPropertyChanged; }
        }
        if (e.NewItems is not null)
        {
            foreach (TargetViewModel t in e.NewItems) { t.PropertyChanged += OnTargetPropertyChanged; }
        }
        RaiseChanged();
    }

    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseChanged();

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all pass (117 prior + 5 new = 122), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): add target and target-bar view-models"
```

---

### Task 2: Node `TargetId` + editor target assignment & badges

**Files:**
- Modify: `BotBuilder.Core/NodeViewModel.cs` (add observable `TargetId`)
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`
- Test: `BotBuilder.Core.Tests/EditorTargetTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/EditorTargetTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class EditorTargetTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void Editor_ExposesATargetBar()
    {
        Assert.NotNull(NewEditor().TargetBar);
    }

    [Fact]
    public void SingleTarget_NoBadges()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        e.TargetBar.AddTarget();

        Assert.Null(node.TargetBadge);
    }

    [Fact]
    public void MultipleTargets_UnassignedNode_BadgesFirstTarget()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        var first = e.TargetBar.AddTarget();
        first.Name = "Client 1";
        e.TargetBar.AddTarget();

        Assert.Equal("Client 1", node.TargetBadge);
    }

    [Fact]
    public void AssignTarget_BadgesAssignedTargetName()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        e.TargetBar.AddTarget();
        var second = e.TargetBar.AddTarget();
        second.Name = "My Phone";

        e.AssignTarget(node, second.Id);

        Assert.Equal(second.Id, node.TargetId);
        Assert.Equal("My Phone", node.TargetBadge);
    }

    [Fact]
    public void RenamingTarget_UpdatesBadges()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        var first = e.TargetBar.AddTarget();
        e.TargetBar.AddTarget();

        first.Name = "Renamed First";

        Assert.Equal("Renamed First", node.TargetBadge);
    }

    [Fact]
    public void RemovingDownToOneTarget_ClearsBadges()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        var a = e.TargetBar.AddTarget();
        var b = e.TargetBar.AddTarget();
        Assert.NotNull(node.TargetBadge);

        e.TargetBar.RemoveTarget(b);

        Assert.Null(node.TargetBadge);
    }

    [Fact]
    public void New_ClearsTargets()
    {
        var e = NewEditor();
        e.TargetBar.AddTarget();
        e.TargetBar.AddTarget();

        e.New();

        Assert.Empty(e.TargetBar.Targets);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `TargetBar`, `AssignTarget`, `NodeViewModel.TargetId` don't exist.

- [ ] **Step 3: Add `TargetId` to `NodeViewModel`**

In `BotBuilder.Core/NodeViewModel.cs`, add an observable `TargetId` alongside the other `[ObservableProperty]` fields (e.g. after `_targetBadge`):
```csharp
    [ObservableProperty] private Guid? _targetId;
```

- [ ] **Step 4: Update `BotEditorViewModel`**

In `BotBuilder.Core/BotEditorViewModel.cs`:

(a) Add the using:
```csharp
using BotBuilder.Core.Targets;
```

(b) In the constructor (after `Viewport = new CanvasViewport();`), create the target bar and subscribe to its changes:
```csharp
        TargetBar = new TargetBarViewModel();
        TargetBar.Changed += OnTargetsChanged;
```

(c) Add the property (near `Palette`/`Viewport`):
```csharp
    public TargetBarViewModel TargetBar { get; }
```

(d) Add assignment + badge methods:
```csharp
    /// <summary>Assigns a node to a target (null = the default first target) and refreshes badges.</summary>
    public void AssignTarget(NodeViewModel node, Guid? targetId)
    {
        node.TargetId = targetId;
        RefreshTargetBadges();
        IsDirty = true;
    }

    /// <summary>Recomputes every node's target badge: shown (the resolved target's name) only when the
    /// bot has more than one target; an unassigned or dangling node resolves to the first target.</summary>
    public void RefreshTargetBadges()
    {
        var targets = TargetBar.Targets;
        if (targets.Count <= 1)
        {
            foreach (var node in Nodes) { node.TargetBadge = null; }
            return;
        }

        foreach (var node in Nodes)
        {
            var resolved = targets.FirstOrDefault(t => t.Id == node.TargetId) ?? targets[0];
            node.TargetBadge = resolved.Name;
        }
    }

    private void OnTargetsChanged(object? sender, EventArgs e)
    {
        RefreshTargetBadges();
        IsDirty = true;
    }
```

(e) In `New()`, clear the targets (add right after `Nodes.Clear();`):
```csharp
        TargetBar.Targets.Clear();
```
(`New()` already sets `IsDirty = false` at its end, so the `OnTargetsChanged`-driven dirty during the clear is reset.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all pass (122 prior + 7 new = 129), 0 failures. Existing tests still green (adding `TargetId`/`TargetBar` doesn't affect prior behavior; `New` clearing targets is additive).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(builder): node target assignment and multi-target badges"
```

---

### Task 3: Persist targets + node `TargetId` in `.bot`

**Files:**
- Modify: `BotBuilder.Core/DocumentMapper.cs`
- Test: `BotBuilder.Core.Tests/DocumentMapperTargetTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/DocumentMapperTargetTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class DocumentMapperTargetTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void ToBot_WritesTargets_WithSelectorInConfig()
    {
        var e = NewEditor();
        var t = e.TargetBar.AddTarget();
        t.Name = "Client 1";
        t.Type = BotTargetType.AndroidDevice;
        t.Selector = "serial:emulator-5554";

        var bot = DocumentMapper.ToBot(e);

        var target = Assert.Single(bot.Targets);
        Assert.Equal(t.Id, target.Id);
        Assert.Equal("Client 1", target.Name);
        Assert.Equal(BotTargetType.AndroidDevice, target.Type);
        Assert.Equal("serial:emulator-5554", target.Config["selector"]);
    }

    [Fact]
    public void ToBot_WritesNodeTargetId()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        var t = e.TargetBar.AddTarget();
        e.TargetBar.AddTarget();
        e.AssignTarget(node, t.Id);

        var bot = DocumentMapper.ToBot(e);

        Assert.Equal(t.Id, bot.Actions.Single(a => a.Id == node.Id).TargetId);
    }

    [Fact]
    public void SaveOpen_RoundTripsTargetsAndAssignment()
    {
        var e = NewEditor();
        var node = e.AddNode("data.log", 10, 10);
        var t1 = e.TargetBar.AddTarget();
        t1.Name = "Client 1";
        t1.Selector = "process:BlueStacks";
        var t2 = e.TargetBar.AddTarget();
        t2.Name = "My Phone";
        e.AssignTarget(node, t2.Id);
        var path = Path.Combine(Path.GetTempPath(), $"adb-m3c2-{Guid.NewGuid():N}.bot");

        try
        {
            e.Save(path);
            var reopened = NewEditor();
            reopened.Open(path);

            Assert.Equal(2, reopened.TargetBar.Targets.Count);
            var phone = reopened.TargetBar.Targets.Single(x => x.Name == "My Phone");
            var client = reopened.TargetBar.Targets.Single(x => x.Name == "Client 1");
            Assert.Equal("process:BlueStacks", client.Selector);

            var loadedNode = reopened.Nodes.Single(n => n.TypeKey == "data.log");
            Assert.Equal(phone.Id, loadedNode.TargetId);
            Assert.Equal("My Phone", loadedNode.TargetBadge); // >1 target, assigned -> badge shows
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: FAIL — `ToBot` doesn't write targets/TargetId yet; round-trip loses them.

- [ ] **Step 3: Update `DocumentMapper`**

In `BotBuilder.Core/DocumentMapper.cs`:

(a) Add usings:
```csharp
using BotBuilder.Core.Targets;
```

(b) In `ToBot`, after writing the actions loop and before (or after) the connections loop, write the node `TargetId` onto each action, and write the targets. First, set `TargetId` when building each action — change the action-creation in the nodes loop to include:
```csharp
                TargetId = node.TargetId,
```
(add that line inside the `new BotAction { ... }` initializer).

Then after the connections loop, add the targets:
```csharp
        foreach (var t in editor.TargetBar.Targets)
        {
            bot.Targets.Add(new BotTarget
            {
                Id = t.Id,
                Name = t.Name,
                Type = t.Type,
                Config = new Dictionary<string, string> { ["selector"] = t.Selector },
            });
        }
```

(c) In `Populate`, after the existing node/connection load (after the `editor.LoadFrom(...)` call), rebuild the target bar and node assignments, then refresh badges:
```csharp
        editor.TargetBar.Targets.Clear();
        foreach (var t in bot.Targets)
        {
            editor.TargetBar.Targets.Add(new TargetViewModel
            {
                Id = t.Id,
                Name = t.Name,
                Type = t.Type,
                Selector = t.Config.TryGetValue("selector", out var sel) ? sel : string.Empty,
            });
        }

        editor.RefreshTargetBadges();
```

(d) The node `TargetId` must be restored when building nodes. In `BuildNode`, after constructing the node (both the known-definition and unknown-typekey branches), set the target id. The simplest is to set it on the returned node before returning — change `BuildNode` so that whichever `NodeViewModel` it creates, it assigns `node.TargetId = action.TargetId;` before returning. For example, capture the node in a local and set it:
```csharp
    private static NodeViewModel BuildNode(BotAction action, ActionRegistry registry)
    {
        NodeViewModel node;
        if (registry.TryGet(action.TypeKey, out var definition) && definition is not null)
        {
            node = NodeViewModel.FromDefinition(
                definition, action.Id, action.Label, action.CanvasPosition.X, action.CanvasPosition.Y);
        }
        else
        {
            node = new NodeViewModel(
                action.Id, action.TypeKey, action.Label, UnknownCategory,
                Array.Empty<PortViewModel>(), Array.Empty<PortViewModel>(),
                action.CanvasPosition.X, action.CanvasPosition.Y);
        }

        node.TargetId = action.TargetId;
        return node;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all pass (129 prior + 3 new = 132), 0 failures. The existing serializer/mapper round-trip tests still pass (targets/TargetId are additive; bots without targets still round-trip — `Bot.Targets` empty, badges null).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): persist targets and node target assignment in .bot"
```

---

### Task 4: WPF — Target Bar chips

> **Verification:** clean `dotnet build` (0 warnings) + `dotnet test` (132) + Manual Verification Checklist.

**Files:**
- Modify: `BotBuilder/MainWindow.xaml`, `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Replace the Target Bar placeholder with chips**

In `BotBuilder/MainWindow.xaml`:

(a) Add a CLR namespace for the targets types to the root `<Window ...>` element (alongside the existing `xmlns:local`):
```xml
        xmlns:targets="clr-namespace:BotBuilder.Core.Targets;assembly=BotBuilder.Core"
```

(b) Replace the existing Target Bar placeholder block:
```xml
        <Border DockPanel.Dock="Top" Background="#EEE" Padding="6" BorderBrush="#CCC" BorderThickness="0,0,0,1">
            <TextBlock Text="Targets: (Target Bar arrives in M3c)" Foreground="#666" />
        </Border>
```
with a real target bar:
```xml
        <Border DockPanel.Dock="Top" Background="#EEE" Padding="6" BorderBrush="#CCC" BorderThickness="0,0,0,1">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="Targets:" VerticalAlignment="Center" Margin="0,0,8,0" FontWeight="Bold" />
                <ItemsControl ItemsSource="{Binding TargetBar.Targets}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Background="White" BorderBrush="#BBB" BorderThickness="1" CornerRadius="4"
                                    Padding="4,2" Margin="0,0,6,0">
                                <StackPanel Orientation="Horizontal">
                                    <TextBox Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}" Width="90"
                                             BorderThickness="0" VerticalAlignment="Center" />
                                    <ComboBox ItemsSource="{x:Static targets:TargetViewModel.AllTypes}"
                                              SelectedItem="{Binding Type}" Margin="4,0" VerticalAlignment="Center" />
                                    <TextBox Text="{Binding Selector, UpdateSourceTrigger=PropertyChanged}" Width="130"
                                             VerticalAlignment="Center" ToolTip="selector" />
                                    <Button Content="x" Click="RemoveTarget_Click" Margin="4,0,0,0" Padding="4,0"
                                            ToolTip="Remove target" />
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <Button Content="+ Add Target" Click="AddTarget_Click" Padding="6,2" />
            </StackPanel>
        </Border>
```

- [ ] **Step 2: Add the click handlers**

In `BotBuilder/MainWindow.xaml.cs`, add:
```csharp
    private void AddTarget_Click(object sender, RoutedEventArgs e) => _editor.TargetBar.AddTarget();

    private void RemoveTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BotBuilder.Core.Targets.TargetViewModel target })
        {
            _editor.TargetBar.RemoveTarget(target);
        }
    }
```

- [ ] **Step 3: Build and test**

Run: `dotnet build ADB.slnx` → expect 0 warnings, 0 errors. (MSB3021/MSB3027 exe-copy-lock means the app is running — report it; compile itself must be clean.)
Run: `dotnet test` → 132 pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(builder): Target Bar chips (add / edit / remove targets)"
```

**Manual Verification Checklist (`dotnet run --project BotBuilder`):**
- The Target Bar shows "Targets:" with an "+ Add Target" button. Clicking it adds a chip (Name textbox, Type dropdown, selector textbox, remove "x").
- Editing a chip's name updates node target badges live; with **2+ targets**, every node card shows a small target-name badge (unassigned nodes show the first target's name).
- Removing back down to one target hides all badges.
- Save a bot with targets, New, then Open it — the targets reappear with their names/types/selectors, and node assignments/badges are restored.

---

## Self-Review

**Spec coverage (M3c-2, completing M3c):**
- Target Bar (chips: add/edit/remove) — Task 1 (VM) + Task 4 (UI). ✓
- Target assignment logic (`AssignTarget`) — Task 2. ✓
- Multi-target node badges — Task 2 (`RefreshTargetBadges`, shown only when >1 target). ✓
- Targets + assignment persisted in `.bot` — Task 3. ✓
- (Per-node assignment UI is the Properties Panel dropdown in M4, as designed; M3c-2 ships the logic + badge display.)

**Placeholder scan:** No TBD/placeholder steps; the only removed placeholder is the Target Bar stub text, replaced by the real bar.

**Type consistency:** `TargetViewModel` (Id/Name/Type/Selector/AllTypes), `TargetBarViewModel` (Targets/AddTarget/RemoveTarget/Changed), `NodeViewModel.TargetId`, editor `TargetBar`/`AssignTarget`/`RefreshTargetBadges`/`OnTargetsChanged`, `DocumentMapper` Targets+TargetId round-trip — names match across tasks and the XAML bindings (`TargetBar.Targets`, `Name`, `Type`, `Selector`, `TargetViewModel.AllTypes`, node `TargetBadge`). ✓

**Scope:** No per-node assignment UI (M4 properties panel), no Test Run/target-picker (M8). Target operations are not on the undo stack (config-bar operations; consistent with the milestone). ✓
