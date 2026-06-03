# M8b — Canvas Run-Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** During a Test Run, highlight each node on the canvas by outcome — green as it succeeds, red on failure — and show an overall run indicator, by folding the runner's per-action log events onto the nodes.

**Architecture:** A testable `RunStatusTracker` (BotBuilder.Core.Integration) folds parsed `RunLogEntry` events into a node-id → `NodeRunState` map plus an overall `RunStatus`. `NodeViewModel` gains an observable `RunState`; the node card's border binds it (via a `MultiBinding` that lets run-state override selection colour). `MainWindow.TestRun_Click` feeds the live entry stream into the tracker and mirrors states onto the node VMs, with a status-bar indicator.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm, xUnit. Reuses M8a's `RunLogEntry`/`RunLogKind`, `RunSession` (multicast `EntryReceived`/`Exited`), and the existing node-card template. Per `Docs/Specs/2026-06-03-m8-integration-design.md` §4.

---

## File Structure

**BotBuilder.Core (new/modified):**
- `NodeRunState.cs` (new) — `enum NodeRunState { None, Succeeded, Failed }`.
- `Integration/RunStatusTracker.cs` (new) — `RunStatus` enum + the tracker.
- `NodeViewModel.cs` (modify) — add `[ObservableProperty] private NodeRunState _runState`.
- `BotEditorViewModel.cs` (modify) — add `ResetRunStates()`.

**BotBuilder (WPF, new/modified):**
- `ValueConverters.cs` (modify) — add `NodeBorderConverter` (IMultiValueConverter).
- `MainWindow.xaml` (modify) — node-card border MultiBinding; a run-status `StatusBarItem`.
- `MainWindow.xaml.cs` (modify) — feed the entry stream into the tracker + node VMs.

**Tests:** `BotBuilder.Core.Tests/Integration/RunStatusTrackerTests.cs`, plus a `ResetRunStates` test in an editor test file.

---

## Task 1: NodeRunState + RunStatusTracker

**Files:**
- Create: `BotBuilder.Core/NodeRunState.cs`, `BotBuilder.Core/Integration/RunStatusTracker.cs`
- Test: `BotBuilder.Core.Tests/Integration/RunStatusTrackerTests.cs`

- [ ] **Step 1: Write the failing tests** — `BotBuilder.Core.Tests/Integration/RunStatusTrackerTests.cs`:

```csharp
using BotBuilder.Core;
using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class RunStatusTrackerTests
{
    private static RunLogEntry Action(string actionId, bool success)
        => new(RunLogKind.Action, actionId, "label", success, success ? null : "err", null, "raw");

    private static RunLogEntry RunStart() => new(RunLogKind.RunStart, null, null, null, null, null, "raw");
    private static RunLogEntry RunEnd(bool success) => new(RunLogKind.RunEnd, null, null, success, null, null, "raw");

    [Fact]
    public void Reset_IsIdleAndEmpty()
    {
        var t = new RunStatusTracker();
        t.Apply(RunStart());
        t.Reset();

        Assert.Equal(RunStatus.Idle, t.Status);
        Assert.Empty(t.NodeStates);
    }

    [Fact]
    public void RunStart_SetsRunning_ClearsNodeStates()
    {
        var t = new RunStatusTracker();
        var id = Guid.NewGuid();
        t.Apply(Action(id.ToString(), success: true));

        t.Apply(RunStart());

        Assert.Equal(RunStatus.Running, t.Status);
        Assert.Empty(t.NodeStates);
    }

    [Fact]
    public void Action_Success_MarksNodeSucceeded_ReturnsId()
    {
        var t = new RunStatusTracker();
        var id = Guid.NewGuid();

        var changed = t.Apply(Action(id.ToString(), success: true));

        Assert.Equal(id, changed);
        Assert.Equal(NodeRunState.Succeeded, t.NodeStates[id]);
    }

    [Fact]
    public void Action_Failure_MarksNodeFailed()
    {
        var t = new RunStatusTracker();
        var id = Guid.NewGuid();

        t.Apply(Action(id.ToString(), success: false));

        Assert.Equal(NodeRunState.Failed, t.NodeStates[id]);
    }

    [Fact]
    public void Action_UnparseableId_IgnoredReturnsNull()
    {
        var t = new RunStatusTracker();

        var changed = t.Apply(Action("not-a-guid", success: true));

        Assert.Null(changed);
        Assert.Empty(t.NodeStates);
    }

    [Theory]
    [InlineData(true, RunStatus.Succeeded)]
    [InlineData(false, RunStatus.Failed)]
    public void RunEnd_SetsOverallStatus(bool success, RunStatus expected)
    {
        var t = new RunStatusTracker();
        t.Apply(RunStart());

        t.Apply(RunEnd(success));

        Assert.Equal(expected, t.Status);
    }

    [Fact]
    public void NonActionEntries_DoNotChangeNodeStates()
    {
        var t = new RunStatusTracker();
        t.Apply(RunStart());

        t.Apply(new RunLogEntry(RunLogKind.Message, null, null, null, null, "hi", "raw"));
        t.Apply(new RunLogEntry(RunLogKind.Unparsed, null, null, null, null, null, "garbage"));

        Assert.Empty(t.NodeStates);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~RunStatusTrackerTests"` → FAIL (types missing).

- [ ] **Step 3: Create `NodeRunState`** — `BotBuilder.Core/NodeRunState.cs`:

```csharp
namespace BotBuilder.Core;

/// <summary>A node's most recent Test Run outcome, used to colour the canvas card.</summary>
public enum NodeRunState { None, Succeeded, Failed }
```

- [ ] **Step 4: Create `RunStatusTracker`** — `BotBuilder.Core/Integration/RunStatusTracker.cs`:

```csharp
namespace BotBuilder.Core.Integration;

/// <summary>Overall state of a Test Run.</summary>
public enum RunStatus { Idle, Running, Succeeded, Failed }

/// <summary>Folds runner log entries into per-node run states and an overall <see cref="Status"/>, for
/// highlighting the canvas during a Test Run.</summary>
public sealed class RunStatusTracker
{
    private readonly Dictionary<Guid, NodeRunState> _nodeStates = new();

    public RunStatus Status { get; private set; } = RunStatus.Idle;
    public IReadOnlyDictionary<Guid, NodeRunState> NodeStates => _nodeStates;

    public void Reset()
    {
        Status = RunStatus.Idle;
        _nodeStates.Clear();
    }

    /// <summary>Updates state from a parsed log entry. Returns the node id whose state changed (so the UI
    /// can repaint just that node), or null when nothing node-specific changed.</summary>
    public Guid? Apply(RunLogEntry entry)
    {
        switch (entry.Kind)
        {
            case RunLogKind.RunStart:
                Status = RunStatus.Running;
                _nodeStates.Clear();
                return null;

            case RunLogKind.Action when Guid.TryParse(entry.ActionId, out var id):
                _nodeStates[id] = entry.Success == true ? NodeRunState.Succeeded : NodeRunState.Failed;
                return id;

            case RunLogKind.RunEnd:
                Status = entry.Success == true ? RunStatus.Succeeded : RunStatus.Failed;
                return null;

            default:
                return null;
        }
    }
}
```

- [ ] **Step 5: Run to verify they pass** — same filter → PASS (8 tests, counting the theory cases).

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/NodeRunState.cs BotBuilder.Core/Integration/RunStatusTracker.cs BotBuilder.Core.Tests/Integration/RunStatusTrackerTests.cs
git commit -m "feat(builder): add RunStatusTracker + NodeRunState (log events -> node/run status)"
```

---

## Task 2: NodeViewModel.RunState + ResetRunStates + node-card highlight

**Files:**
- Modify: `BotBuilder.Core/NodeViewModel.cs`, `BotBuilder.Core/BotEditorViewModel.cs`
- Test: `BotBuilder.Core.Tests/BotEditorViewModelRunStateTests.cs` (new)
- Modify: `BotBuilder/ValueConverters.cs`, `BotBuilder/MainWindow.xaml`

- [ ] **Step 1: Add `RunState` to `NodeViewModel`.** In `BotBuilder.Core/NodeViewModel.cs`, add to the
  existing `[ObservableProperty]` field block (just after `private int _retryDelayMs;`):

```csharp
    [ObservableProperty] private NodeRunState _runState;
```

- [ ] **Step 2: Write the failing test** — `BotBuilder.Core.Tests/BotEditorViewModelRunStateTests.cs`:

```csharp
using AdbCore.Actions;
using BotBuilder.Core;

namespace BotBuilder.Core.Tests;

public class BotEditorViewModelRunStateTests
{
    [Fact]
    public void ResetRunStates_ClearsEveryNode()
    {
        var editor = new BotEditorViewModel(new ActionRegistry());
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("control.end", 100, 0);
        a.RunState = NodeRunState.Succeeded;
        b.RunState = NodeRunState.Failed;

        editor.ResetRunStates();

        Assert.Equal(NodeRunState.None, a.RunState);
        Assert.Equal(NodeRunState.None, b.RunState);
    }
}
```

> `control.start` and `control.end` are the registered built-in `TypeKey`s for Start/End (verified). The
> test only needs two nodes.

- [ ] **Step 3: Run to verify it fails** — `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~BotEditorViewModelRunStateTests"` → FAIL (`ResetRunStates` missing).

- [ ] **Step 4: Add `ResetRunStates` to `BotEditorViewModel`.** In `BotBuilder.Core/BotEditorViewModel.cs`,
  add this public method (near the other node operations):

```csharp
    /// <summary>Clears every node's Test Run highlight (called when a run starts or the graph is edited).</summary>
    public void ResetRunStates()
    {
        foreach (var node in Nodes)
        {
            node.RunState = NodeRunState.None;
        }
    }
```

- [ ] **Step 5: Run to verify it passes** — same filter → PASS. Then
  `dotnet build BotBuilder.Core/BotBuilder.Core.csproj -c Debug --nologo` → 0 warnings.

- [ ] **Step 6: Add `NodeBorderConverter`** to `BotBuilder/ValueConverters.cs` (append; keep existing
  converters). It mirrors `SelectionBorderConverter`'s colours and lets run-state override selection:

```csharp
/// <summary>[IsSelected (bool), RunState (NodeRunState)] -> node-card border brush. Run outcome wins over
/// selection: Failed = red, Succeeded = green, else the selection colour (blue selected / grey not).</summary>
public sealed class NodeBorderConverter : System.Windows.Data.IMultiValueConverter
{
    public static readonly NodeBorderConverter Instance = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = values.Length > 0 && values[0] is true;
        var run = values.Length > 1 && values[1] is BotBuilder.Core.NodeRunState s
            ? s
            : BotBuilder.Core.NodeRunState.None;

        return run switch
        {
            BotBuilder.Core.NodeRunState.Failed => new SolidColorBrush(Color.FromRgb(0xD6, 0x29, 0x29)),
            BotBuilder.Core.NodeRunState.Succeeded => new SolidColorBrush(Color.FromRgb(0x29, 0xA0, 0x4A)),
            _ => new SolidColorBrush(selected ? Color.FromRgb(0x29, 0x6F, 0xD6) : Color.FromRgb(0xDD, 0xDD, 0xDD)),
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 7: Bind the node-card border to run-state.** In `BotBuilder/MainWindow.xaml`, find the node
  card `Border` (the one with `Width="160"` and
  `BorderBrush="{Binding IsSelected, Converter={x:Static local:SelectionBorderConverter.Instance}}"`).
  Replace that single `BorderBrush="..."` attribute with a `MultiBinding` element:

```xml
                                <Border Width="160" MinHeight="70" CornerRadius="6" Background="White"
                                        BorderThickness="2">
                                    <Border.BorderBrush>
                                        <MultiBinding Converter="{x:Static local:NodeBorderConverter.Instance}">
                                            <Binding Path="IsSelected" />
                                            <Binding Path="RunState" />
                                        </MultiBinding>
                                    </Border.BorderBrush>
```

  (Leave the rest of the node template — header, ports, content — exactly as it was; only the opening
  `Border` tag's `BorderBrush` attribute becomes the nested `<Border.BorderBrush>` element.)

- [ ] **Step 8: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add BotBuilder.Core/NodeViewModel.cs BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core.Tests/BotEditorViewModelRunStateTests.cs BotBuilder/ValueConverters.cs BotBuilder/MainWindow.xaml
git commit -m "feat(builder): node RunState + ResetRunStates + run-state node border"
```

---

## Task 3: Wire run-status into Test Run (WPF, visual)

Feeds the live `RunSession` entry stream into a `RunStatusTracker`, mirrors node states onto the node
VMs, and shows an overall run indicator in the status bar. No unit tests.

**Files:**
- Modify: `BotBuilder/MainWindow.xaml`, `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Add a run-status indicator to the status bar.** In `BotBuilder/MainWindow.xaml`, inside the
  existing `<StatusBar ...>` (which already has BotName + node-count items), add an item:

```xml
            <StatusBarItem><TextBlock x:Name="RunStatusText" Foreground="#555" /></StatusBarItem>
```

- [ ] **Step 2: Add tracker state + the run-status feed to `MainWindow.xaml.cs`.** Add two fields to the
  `MainWindow` class:

```csharp
    private readonly BotBuilder.Core.Integration.RunStatusTracker _runStatus = new();
    private System.Collections.Generic.Dictionary<System.Guid, BotBuilder.Core.NodeViewModel> _runNodeById = new();
```

- [ ] **Step 3: Hook the session in `TestRun_Click`.** Replace the final line of `TestRun_Click`
  (`LogPanel.Attach(RunSession.Start(exe, args));`) with:

```csharp
        var session = RunSession.Start(exe, args);
        BeginRunStatus(session);
        LogPanel.Visibility = Visibility.Visible;
        LogPanel.Attach(session);
```

- [ ] **Step 4: Add the run-status methods** to `MainWindow.xaml.cs`:

```csharp
    private void BeginRunStatus(RunSession session)
    {
        _editor.ResetRunStates();
        _runNodeById = _editor.Nodes.ToDictionary(n => n.Id);
        _runStatus.Reset();
        RunStatusText.Text = "Run: starting…";

        session.EntryReceived += OnRunStatusEntry;
        session.Exited += OnRunStatusExited;
    }

    private void OnRunStatusEntry(object? sender, BotBuilder.Core.Integration.RunLogEntry entry)
    {
        var changedId = _runStatus.Apply(entry);
        if (changedId is System.Guid id && _runNodeById.TryGetValue(id, out var node))
        {
            node.RunState = _runStatus.NodeStates[id];
        }

        RunStatusText.Text = _runStatus.Status switch
        {
            BotBuilder.Core.Integration.RunStatus.Running => "Run: running…",
            BotBuilder.Core.Integration.RunStatus.Succeeded => "Run: succeeded",
            BotBuilder.Core.Integration.RunStatus.Failed => "Run: failed",
            _ => string.Empty,
        };
    }

    private void OnRunStatusExited(object? sender, int code)
    {
        // If the run was stopped/crashed without a run-end line, reflect that.
        if (_runStatus.Status == BotBuilder.Core.Integration.RunStatus.Running)
        {
            RunStatusText.Text = code == 0 ? "Run: finished" : $"Run: stopped (exit {code})";
        }
    }
```

> Note: `RunSession` raises `EntryReceived`/`Exited` on the UI thread (it captures the UI
> `SynchronizationContext`), so these handlers can touch `RunStatusText` and node VMs directly. The node
> card's `MultiBinding` repaints automatically when a node's `RunState` changes.

- [ ] **Step 5: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git commit -m "feat(builder): feed Test Run log stream into canvas node highlighting + run indicator"
```

---

## Task 4: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution** — `dotnet build ADB.slnx -c Debug --nologo` → `0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run the whole test suite** — `dotnet test ADB.slnx -c Debug --nologo --no-build`. New
  BotBuilder.Core tests this slice: RunStatusTracker 8 (incl. theory), ResetRunStates 1 = +9.
  BotBuilder.Core.Tests should total 133 (was 124). AdbCore 226 / BotCapture.Core 41 / BotRunner 19 unchanged.

- [ ] **Step 3: Manual run (user visual verification).** `dotnet run --project BotBuilder/BotBuilder.csproj -c Debug`:
  - Build a small bot (e.g. Start → Log → End), **Run → Test Run** (or F5), pick a window, **Run**.
  - As the run streams, each executed node's card border flashes **green** (succeeded); if an action
    fails, that node turns **red**. The status bar shows **Run: running… → succeeded / failed**.
  - Starting another Test Run clears the previous highlights (they reset at run start). *(Clearing on
    graph edits is a deferred minor — M8b resets at run start only.)*

> Hand off to the user for visual confirmation before opening the PR.
