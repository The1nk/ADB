# M3b — Connections, Delete, Undo/Redo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add port-to-port connections (with validation incl. cycle prevention) rendered as bezier curves, node/connection deletion, and a full undo/redo stack covering every editing operation, to the Bot Builder.

**Architecture:** All logic stays in the WPF-free `BotBuilder.Core` and is unit-tested: connection geometry, connection validation, an `UndoStack` of `IUndoableCommand`s, and a refactor of the editor so Add/Move/Connect/Disconnect/Delete all flow through the stack. The WPF shell gains a connection-rendering layer and gestures (drag port→port to connect, click+Delete to remove, Ctrl+Z/Y to undo/redo), all thin delegations to the core. Per the approved spec `Docs/Specs/2026-06-01-m3-builder-canvas-design.md` (§3.2, §3.3, M3b slice).

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm, xUnit. Builds on merged M3a.

---

## Verification model
- **Tasks 1–5 (`BotBuilder.Core`)**: strict TDD, verified by `dotnet test`.
- **Tasks 6–7 (`BotBuilder` WPF)**: `dotnet build ADB.slnx` 0 warnings + `dotnet test` green; each ends with a **Manual Verification Checklist** for the user.

## File Structure
```
BotBuilder.Core/
  CanvasPoint.cs              # NEW: WPF-free 2D point
  NodeLayout.cs               # NEW: shared card/port layout constants + anchor math
  PortViewModel.cs            # MODIFIED: add AnchorOffset
  NodeViewModel.cs            # MODIFIED: assign port anchors in FromDefinition
  Connections/
    ConnectionGeometry.cs     # NEW: bezier path-string builder (pure)
    ConnectionViewModel.cs    # NEW: a connection, computes PathData, tracks selection
    ConnectionError.cs        # NEW: validation result enum
    ConnectionValidator.cs    # NEW: output->input / self / dup / cycle checks (pure)
  Undo/
    IUndoableCommand.cs       # NEW
    UndoStack.cs              # NEW: Execute/PushExecuted/Undo/Redo/CanUndo/CanRedo
    EditorCommands.cs         # NEW: Add/Move/Connect/Disconnect/DeleteNode commands
  BotEditorViewModel.cs       # MODIFIED: Connections, undoable ops, selection, Undo/Redo
BotBuilder/
  MainWindow.xaml             # MODIFIED: connection layer + revised card ports + Edit menu
  MainWindow.xaml.cs          # MODIFIED: connect/delete/undo gestures
BotBuilder.Core.Tests/
  NodeLayoutTests.cs, ConnectionGeometryTests.cs, ConnectionViewModelTests.cs,
  ConnectionValidatorTests.cs, UndoStackTests.cs, EditorConnectionsTests.cs
```

---

### Task 1: `CanvasPoint`, `NodeLayout`, and port anchors

**Files:**
- Create: `BotBuilder.Core/CanvasPoint.cs`, `BotBuilder.Core/NodeLayout.cs`
- Modify: `BotBuilder.Core/PortViewModel.cs`, `BotBuilder.Core/NodeViewModel.cs`
- Test: `BotBuilder.Core.Tests/NodeLayoutTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/NodeLayoutTests.cs`:
```csharp
using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeLayoutTests
{
    [Fact]
    public void InputAnchor_IsOnLeftEdge_OutputAnchor_OnRightEdge()
    {
        var input = NodeLayout.InputAnchor(0);
        var output = NodeLayout.OutputAnchor(0);

        Assert.Equal(0, input.X);
        Assert.Equal(NodeLayout.CardWidth, output.X);
        Assert.Equal(input.Y, output.Y); // same row for index 0
    }

    [Fact]
    public void Anchors_StackVerticallyByIndex()
    {
        var a0 = NodeLayout.InputAnchor(0);
        var a1 = NodeLayout.InputAnchor(1);

        Assert.Equal(NodeLayout.PortSpacing, a1.Y - a0.Y);
    }

    [Fact]
    public void FromDefinition_AssignsPortAnchorOffsets()
    {
        var node = NodeViewModel.FromDefinition(new LogAction(), Guid.NewGuid(), "", 0, 0);

        Assert.Equal(NodeLayout.InputAnchor(0), node.InputPorts[0].AnchorOffset);
        Assert.Equal(NodeLayout.OutputAnchor(0), node.OutputPorts[0].AnchorOffset);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `CanvasPoint`, `NodeLayout`, `PortViewModel.AnchorOffset` don't exist.

- [ ] **Step 3: Implement the new types and modify the port/node VMs**

Create `BotBuilder.Core/CanvasPoint.cs`:
```csharp
namespace BotBuilder.Core;

/// <summary>A WPF-free 2D point in canvas coordinates.</summary>
public readonly record struct CanvasPoint(double X, double Y);
```

Create `BotBuilder.Core/NodeLayout.cs`:
```csharp
namespace BotBuilder.Core;

/// <summary>Shared geometry constants for node cards, used by both the view (rendering)
/// and the core (connection anchor math) so connection endpoints line up with rendered ports.</summary>
public static class NodeLayout
{
    public const double CardWidth = 160;
    public const double HeaderHeight = 28;
    public const double PortAreaTop = HeaderHeight + 12;
    public const double PortSpacing = 20;
    public const double PortRadius = 5;

    /// <summary>Anchor of the input port at <paramref name="index"/>, relative to the card's top-left.</summary>
    public static CanvasPoint InputAnchor(int index) => new(0, PortAreaTop + index * PortSpacing);

    /// <summary>Anchor of the output port at <paramref name="index"/>, relative to the card's top-left.</summary>
    public static CanvasPoint OutputAnchor(int index) => new(CardWidth, PortAreaTop + index * PortSpacing);
}
```

Overwrite `BotBuilder.Core/PortViewModel.cs`:
```csharp
namespace BotBuilder.Core;

/// <summary>A single input or output port shown on a node card.</summary>
public sealed class PortViewModel
{
    public PortViewModel(string name, PortDirection direction, CanvasPoint anchorOffset)
    {
        Name = name;
        Direction = direction;
        AnchorOffset = anchorOffset;
    }

    public string Name { get; }
    public PortDirection Direction { get; }

    /// <summary>Position of this port relative to the card's top-left corner.</summary>
    public CanvasPoint AnchorOffset { get; }
}
```

In `BotBuilder.Core/NodeViewModel.cs`, replace the `FromDefinition` method body so ports are built with indexed anchors (leave the rest of the class unchanged):
```csharp
    /// <summary>Builds a node from an action definition, deriving ports/category from it.</summary>
    public static NodeViewModel FromDefinition(IActionDefinition definition, Guid id, string label, double x, double y)
    {
        var inputs = definition.InputPorts
            .Select((p, i) => new PortViewModel(p.Name, PortDirection.In, NodeLayout.InputAnchor(i)))
            .ToList();
        var outputs = definition.OutputPorts
            .Select((p, i) => new PortViewModel(p.Name, PortDirection.Out, NodeLayout.OutputAnchor(i)))
            .ToList();

        return new NodeViewModel(
            id,
            definition.TypeKey,
            string.IsNullOrEmpty(label) ? definition.DisplayName : label,
            definition.Category,
            inputs,
            outputs,
            x,
            y);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests pass (74 prior + 3 new = 77), 0 failures. (The M3a `NodeViewModelTests` still pass — `PortViewModel.Direction`/`Name` are unchanged and `FromDefinition` still derives the same ports.)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): add canvas-point, node layout, and port anchor offsets"
```

---

### Task 2: `ConnectionGeometry` + `ConnectionViewModel`

**Files:**
- Create: `BotBuilder.Core/Connections/ConnectionGeometry.cs`, `BotBuilder.Core/Connections/ConnectionViewModel.cs`
- Test: `BotBuilder.Core.Tests/ConnectionGeometryTests.cs`, `ConnectionViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/ConnectionGeometryTests.cs`:
```csharp
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class ConnectionGeometryTests
{
    [Fact]
    public void BuildPath_ProducesInvariantCultureBezierString()
    {
        var path = ConnectionGeometry.BuildPath(new CanvasPoint(0, 0), new CanvasPoint(100, 50));

        Assert.StartsWith("M 0,0 C ", path);
        Assert.Contains(",", path);
        Assert.DoesNotContain(";", path);       // no locale decimal-comma corruption
        Assert.Equal(path, path.Trim());
    }

    [Fact]
    public void ControlPoints_ExtendHorizontallyByAtLeastMinimum()
    {
        var (c1, c2) = ConnectionGeometry.ControlPoints(new CanvasPoint(0, 0), new CanvasPoint(10, 0));

        Assert.True(c1.X >= 40);              // min horizontal pull
        Assert.Equal(0, c1.Y);
        Assert.Equal(10 - (c1.X - 0), c2.X, 3);
    }
}
```

Create `BotBuilder.Core.Tests/ConnectionViewModelTests.cs`:
```csharp
using System.ComponentModel;
using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class ConnectionViewModelTests
{
    private static (NodeViewModel src, NodeViewModel tgt) TwoNodes()
        => (NodeViewModel.FromDefinition(new StartAction(), Guid.NewGuid(), "", 0, 0),
            NodeViewModel.FromDefinition(new LogAction(), Guid.NewGuid(), "", 300, 100));

    [Fact]
    public void PathData_ReflectsEndpointAnchors()
    {
        var (src, tgt) = TwoNodes();
        var conn = new ConnectionViewModel(Guid.NewGuid(), src, src.OutputPorts[0], tgt, tgt.InputPorts[0]);

        // start = src.X + outputAnchor.X (=CardWidth), so the path must begin there
        var expectedStartX = src.X + src.OutputPorts[0].AnchorOffset.X;
        Assert.Contains($"M {expectedStartX},", conn.PathData);
    }

    [Fact]
    public void MovingSourceNode_RaisesPathDataChanged()
    {
        var (src, tgt) = TwoNodes();
        var conn = new ConnectionViewModel(Guid.NewGuid(), src, src.OutputPorts[0], tgt, tgt.InputPorts[0]);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)conn).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        src.X += 25;

        Assert.Contains(nameof(ConnectionViewModel.PathData), raised);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `ConnectionGeometry`, `ConnectionViewModel` don't exist.

- [ ] **Step 3: Implement geometry and the connection VM**

Create `BotBuilder.Core/Connections/ConnectionGeometry.cs`:
```csharp
using System.Globalization;

namespace BotBuilder.Core.Connections;

/// <summary>Pure functions for routing a connection as a horizontal cubic bezier.</summary>
public static class ConnectionGeometry
{
    private const double MinHorizontalPull = 40;

    /// <summary>The two cubic control points for a curve from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public static (CanvasPoint C1, CanvasPoint C2) ControlPoints(CanvasPoint start, CanvasPoint end)
    {
        var pull = Math.Max(MinHorizontalPull, Math.Abs(end.X - start.X) / 2);
        return (new CanvasPoint(start.X + pull, start.Y), new CanvasPoint(end.X - pull, end.Y));
    }

    /// <summary>A WPF path mini-language string ("M .. C .. .. ..") in invariant culture.</summary>
    public static string BuildPath(CanvasPoint start, CanvasPoint end)
    {
        var (c1, c2) = ControlPoints(start, end);
        return string.Create(CultureInfo.InvariantCulture,
            $"M {start.X},{start.Y} C {c1.X},{c1.Y} {c2.X},{c2.Y} {end.X},{end.Y}");
    }
}
```

Create `BotBuilder.Core/Connections/ConnectionViewModel.cs`:
```csharp
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Connections;

/// <summary>A directed connection between an output port and an input port. Recomputes its
/// bezier <see cref="PathData"/> whenever either endpoint node moves.</summary>
public partial class ConnectionViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public ConnectionViewModel(
        Guid id,
        NodeViewModel source,
        PortViewModel sourcePort,
        NodeViewModel target,
        PortViewModel targetPort)
    {
        Id = id;
        Source = source;
        SourcePort = sourcePort;
        Target = target;
        TargetPort = targetPort;

        Source.PropertyChanged += OnEndpointMoved;
        Target.PropertyChanged += OnEndpointMoved;
    }

    public Guid Id { get; }
    public NodeViewModel Source { get; }
    public PortViewModel SourcePort { get; }
    public NodeViewModel Target { get; }
    public PortViewModel TargetPort { get; }

    public string PathData => ConnectionGeometry.BuildPath(Anchor(Source, SourcePort), Anchor(Target, TargetPort));

    /// <summary>Detaches endpoint subscriptions; call when the connection is removed.</summary>
    public void Detach()
    {
        Source.PropertyChanged -= OnEndpointMoved;
        Target.PropertyChanged -= OnEndpointMoved;
    }

    private void OnEndpointMoved(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NodeViewModel.X) or nameof(NodeViewModel.Y))
        {
            OnPropertyChanged(nameof(PathData));
        }
    }

    private static CanvasPoint Anchor(NodeViewModel node, PortViewModel port)
        => new(node.X + port.AnchorOffset.X, node.Y + port.AnchorOffset.Y);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests pass (77 prior + 4 new = 81), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): add connection geometry and connection view-model"
```

---

### Task 3: `ConnectionValidator` (incl. cycle detection)

**Files:**
- Create: `BotBuilder.Core/Connections/ConnectionError.cs`, `BotBuilder.Core/Connections/ConnectionValidator.cs`
- Test: `BotBuilder.Core.Tests/ConnectionValidatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/ConnectionValidatorTests.cs`:
```csharp
using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class ConnectionValidatorTests
{
    private static NodeViewModel Log() => NodeViewModel.FromDefinition(new LogAction(), Guid.NewGuid(), "", 0, 0);

    private static ConnectionViewModel Edge(NodeViewModel s, NodeViewModel t)
        => new(Guid.NewGuid(), s, s.OutputPorts[0], t, t.InputPorts[0]);

    [Fact]
    public void Valid_OutputToInput_IsAllowed()
    {
        var a = Log();
        var b = Log();

        var result = ConnectionValidator.Validate(Array.Empty<ConnectionViewModel>(),
            a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.None, result);
    }

    [Fact]
    public void OutputToOutput_IsRejected()
    {
        var a = Log();
        var b = Log();

        var result = ConnectionValidator.Validate(Array.Empty<ConnectionViewModel>(),
            a, a.OutputPorts[0], b, b.OutputPorts[0]);

        Assert.Equal(ConnectionError.NotOutputToInput, result);
    }

    [Fact]
    public void SelfConnection_IsRejected()
    {
        var a = Log();

        var result = ConnectionValidator.Validate(Array.Empty<ConnectionViewModel>(),
            a, a.OutputPorts[0], a, a.InputPorts[0]);

        Assert.Equal(ConnectionError.SelfConnection, result);
    }

    [Fact]
    public void DuplicateEdge_IsRejected()
    {
        var a = Log();
        var b = Log();
        var existing = new[] { Edge(a, b) };

        var result = ConnectionValidator.Validate(existing,
            a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.Duplicate, result);
    }

    [Fact]
    public void Cycle_IsRejected()
    {
        var a = Log();
        var b = Log();
        var c = Log();
        // a -> b -> c already; adding c -> a would close a cycle
        var existing = new[] { Edge(a, b), Edge(b, c) };

        var result = ConnectionValidator.Validate(existing,
            c, c.OutputPorts[0], a, a.InputPorts[0]);

        Assert.Equal(ConnectionError.WouldCreateCycle, result);
    }

    [Fact]
    public void NonCycle_ParallelPath_IsAllowed()
    {
        var a = Log();
        var b = Log();
        var c = Log();
        // a -> b ; a -> c ; adding b -> c is a DAG, not a cycle
        var existing = new[] { Edge(a, b), Edge(a, c) };

        var result = ConnectionValidator.Validate(existing,
            b, b.OutputPorts[0], c, c.InputPorts[0]);

        Assert.Equal(ConnectionError.None, result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `ConnectionError`, `ConnectionValidator` don't exist.

- [ ] **Step 3: Implement the validator**

Create `BotBuilder.Core/Connections/ConnectionError.cs`:
```csharp
namespace BotBuilder.Core.Connections;

/// <summary>Why a proposed connection was rejected (or <see cref="None"/> if allowed).</summary>
public enum ConnectionError
{
    None,
    NotOutputToInput,
    SelfConnection,
    Duplicate,
    WouldCreateCycle,
}
```

Create `BotBuilder.Core/Connections/ConnectionValidator.cs`:
```csharp
namespace BotBuilder.Core.Connections;

/// <summary>Validates a proposed connection against the DAG rules (output→input, no self,
/// no duplicate, no cycle).</summary>
public static class ConnectionValidator
{
    public static ConnectionError Validate(
        IReadOnlyCollection<ConnectionViewModel> existing,
        NodeViewModel source,
        PortViewModel sourcePort,
        NodeViewModel target,
        PortViewModel targetPort)
    {
        if (sourcePort.Direction != PortDirection.Out || targetPort.Direction != PortDirection.In)
        {
            return ConnectionError.NotOutputToInput;
        }

        if (ReferenceEquals(source, target))
        {
            return ConnectionError.SelfConnection;
        }

        if (existing.Any(c =>
                ReferenceEquals(c.Source, source) && c.SourcePort.Name == sourcePort.Name &&
                ReferenceEquals(c.Target, target) && c.TargetPort.Name == targetPort.Name))
        {
            return ConnectionError.Duplicate;
        }

        if (TargetReachesSource(existing, source, target))
        {
            return ConnectionError.WouldCreateCycle;
        }

        return ConnectionError.None;
    }

    // Adding source->target creates a cycle iff target can already reach source.
    private static bool TargetReachesSource(
        IReadOnlyCollection<ConnectionViewModel> existing, NodeViewModel source, NodeViewModel target)
    {
        var visited = new HashSet<NodeViewModel>();
        var stack = new Stack<NodeViewModel>();
        stack.Push(target);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (ReferenceEquals(node, source))
            {
                return true;
            }
            if (!visited.Add(node))
            {
                continue;
            }
            foreach (var edge in existing.Where(c => ReferenceEquals(c.Source, node)))
            {
                stack.Push(edge.Target);
            }
        }

        return false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests pass (81 prior + 6 new = 87), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): add connection validation with cycle detection"
```

---

### Task 4: `UndoStack` + `IUndoableCommand`

**Files:**
- Create: `BotBuilder.Core/Undo/IUndoableCommand.cs`, `BotBuilder.Core/Undo/UndoStack.cs`
- Test: `BotBuilder.Core.Tests/UndoStackTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/UndoStackTests.cs`:
```csharp
using BotBuilder.Core.Undo;
using Xunit;

namespace BotBuilder.Core.Tests;

public class UndoStackTests
{
    private sealed class Counter : IUndoableCommand
    {
        private readonly Action _inc;
        private readonly Action _dec;
        public Counter(Action inc, Action dec) { _inc = inc; _dec = dec; }
        public void Do() => _inc();
        public void Undo() => _dec();
    }

    [Fact]
    public void Execute_RunsDo_AndEnablesUndo()
    {
        var stack = new UndoStack();
        var n = 0;
        stack.Execute(new Counter(() => n++, () => n--));

        Assert.Equal(1, n);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Undo_ThenRedo_RestoresState()
    {
        var stack = new UndoStack();
        var n = 0;
        stack.Execute(new Counter(() => n++, () => n--));

        stack.Undo();
        Assert.Equal(0, n);
        Assert.True(stack.CanRedo);

        stack.Redo();
        Assert.Equal(1, n);
    }

    [Fact]
    public void PushExecuted_DoesNotRunDo_ButIsUndoable()
    {
        var stack = new UndoStack();
        var n = 5; // pretend the change was already applied live (e.g. a drag)
        stack.PushExecuted(new Counter(() => n++, () => n--));

        Assert.Equal(5, n);          // Do() was NOT called
        Assert.True(stack.CanUndo);

        stack.Undo();
        Assert.Equal(4, n);
    }

    [Fact]
    public void NewExecute_ClearsRedo()
    {
        var stack = new UndoStack();
        var n = 0;
        stack.Execute(new Counter(() => n++, () => n--));
        stack.Undo();
        Assert.True(stack.CanRedo);

        stack.Execute(new Counter(() => n += 10, () => n -= 10));

        Assert.False(stack.CanRedo);
        Assert.Equal(10, n);
    }

    [Fact]
    public void Clear_ResetsBothStacks()
    {
        var stack = new UndoStack();
        stack.Execute(new Counter(() => { }, () => { }));

        stack.Clear();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `UndoStack`, `IUndoableCommand` don't exist.

- [ ] **Step 3: Implement the stack**

Create `BotBuilder.Core/Undo/IUndoableCommand.cs`:
```csharp
namespace BotBuilder.Core.Undo;

/// <summary>A reversible editing operation.</summary>
public interface IUndoableCommand
{
    /// <summary>Apply (or re-apply, on redo) the change.</summary>
    void Do();

    /// <summary>Reverse the change.</summary>
    void Undo();
}
```

Create `BotBuilder.Core/Undo/UndoStack.cs`:
```csharp
namespace BotBuilder.Core.Undo;

/// <summary>A linear undo/redo history of <see cref="IUndoableCommand"/>s.</summary>
public sealed class UndoStack
{
    private readonly Stack<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Runs the command's <see cref="IUndoableCommand.Do"/>, then records it.</summary>
    public void Execute(IUndoableCommand command)
    {
        command.Do();
        PushExecuted(command);
    }

    /// <summary>Records a command whose effect was already applied (e.g. a live drag).</summary>
    public void PushExecuted(IUndoableCommand command)
    {
        _undo.Push(command);
        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }
        var command = _undo.Pop();
        command.Undo();
        _redo.Push(command);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }
        var command = _redo.Pop();
        command.Do();
        _undo.Push(command);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests pass (87 prior + 5 new = 92), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): add undo/redo stack"
```

---

### Task 5: Editor — undoable operations, connections, selection

Refactors Add/Move and adds Connect/Disconnect/Delete as undoable commands, plus connection selection and Undo/Redo. The editing commands live in one file and operate on `internal` mutation helpers on the editor.

**Files:**
- Create: `BotBuilder.Core/Undo/EditorCommands.cs`
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`
- Modify: `BotBuilder.Core/DocumentMapper.cs` (persist connections in `ToBot`/`Populate`)
- Test: `BotBuilder.Core.Tests/EditorConnectionsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/EditorConnectionsTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class EditorConnectionsTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static (NodeViewModel a, NodeViewModel b) TwoNodes(BotEditorViewModel e)
        => ((NodeViewModel)e.AddNode("control.start", 0, 0), (NodeViewModel)e.AddNode("data.log", 200, 0));

    [Fact]
    public void Connect_Valid_AddsConnection_AndIsUndoable()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);

        var result = e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.None, result);
        Assert.Single(e.Connections);

        e.Undo();
        Assert.Empty(e.Connections);
        e.Redo();
        Assert.Single(e.Connections);
    }

    [Fact]
    public void Connect_Invalid_DoesNotAdd_AndReturnsReason()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);

        var result = e.Connect(a, a.OutputPorts[0], b, b.OutputPorts[0]); // output->output

        Assert.Equal(ConnectionError.NotOutputToInput, result);
        Assert.Empty(e.Connections);
    }

    [Fact]
    public void DeleteNode_CascadesConnections_AndUndoRestoresThem()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);

        e.DeleteNode(a);
        Assert.DoesNotContain(a, e.Nodes);
        Assert.Empty(e.Connections);

        e.Undo();
        Assert.Contains(a, e.Nodes);
        Assert.Single(e.Connections);
    }

    [Fact]
    public void Disconnect_RemovesConnection_AndUndoRestores()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);
        var conn = e.Connections[0];

        e.Disconnect(conn);
        Assert.Empty(e.Connections);

        e.Undo();
        Assert.Single(e.Connections);
    }

    [Fact]
    public void MoveThenUndo_RestoresOldPosition()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 10, 10);

        e.MoveNode(node, 99, 99);     // live drag update
        e.CommitMove(node, 10, 10);   // committed on mouse-up

        e.Undo();
        Assert.Equal(10, node.X);
        Assert.Equal(10, node.Y);
        e.Redo();
        Assert.Equal(99, node.X);
    }

    [Fact]
    public void DeleteSelection_RemovesSelectedConnectionElseSelectedNode()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);

        e.SelectConnection(e.Connections[0]);
        e.DeleteSelection();
        Assert.Empty(e.Connections);
        Assert.Equal(2, e.Nodes.Count);

        e.Select(a);
        e.DeleteSelection();
        Assert.DoesNotContain(a, e.Nodes);
    }

    [Fact]
    public void Selecting_Node_And_Connection_AreMutuallyExclusive()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);
        var conn = e.Connections[0];

        e.Select(a);
        Assert.Null(e.SelectedConnection);

        e.SelectConnection(conn);
        Assert.Null(e.SelectedNode);
        Assert.False(a.IsSelected);
        Assert.True(conn.IsSelected);
    }

    [Fact]
    public void SaveOpen_RoundTripsConnections()
    {
        var e = NewEditor();
        var (a, b) = TwoNodes(e);
        e.Connect(a, a.OutputPorts[0], b, b.InputPorts[0]);
        var path = Path.Combine(Path.GetTempPath(), $"adb-m3b-{Guid.NewGuid():N}.bot");

        try
        {
            e.Save(path);
            var reopened = NewEditor();
            reopened.Open(path);

            Assert.Equal(2, reopened.Nodes.Count);
            Assert.Single(reopened.Connections);
            var c = reopened.Connections[0];
            Assert.Equal("control.start", c.Source.TypeKey);
            Assert.Equal("data.log", c.Target.TypeKey);
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
Expected: build FAILS — `Connect`, `Disconnect`, `DeleteNode`, `CommitMove`, `DeleteSelection`, `SelectConnection`, `Connections`, `SelectedConnection`, `Undo`, `Redo` don't exist on the editor.

- [ ] **Step 3: Implement the editor commands**

Create `BotBuilder.Core/Undo/EditorCommands.cs`:
```csharp
using BotBuilder.Core.Connections;

namespace BotBuilder.Core.Undo;

/// <summary>Adds a node; undo removes it.</summary>
internal sealed class AddNodeCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly NodeViewModel _node;
    public AddNodeCommand(BotEditorViewModel editor, NodeViewModel node) { _editor = editor; _node = node; }
    public void Do() => _editor.AddNodeCore(_node);
    public void Undo() => _editor.RemoveNodeCore(_node);
}

/// <summary>Records a node move (effect already applied live); undo/redo toggle position.</summary>
internal sealed class MoveNodeCommand : IUndoableCommand
{
    private readonly NodeViewModel _node;
    private readonly double _oldX, _oldY, _newX, _newY;
    public MoveNodeCommand(NodeViewModel node, double oldX, double oldY, double newX, double newY)
    { _node = node; _oldX = oldX; _oldY = oldY; _newX = newX; _newY = newY; }
    public void Do() { _node.X = _newX; _node.Y = _newY; }
    public void Undo() { _node.X = _oldX; _node.Y = _oldY; }
}

/// <summary>Adds a connection; undo removes it (and detaches its subscriptions).</summary>
internal sealed class ConnectCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly ConnectionViewModel _connection;
    public ConnectCommand(BotEditorViewModel editor, ConnectionViewModel connection) { _editor = editor; _connection = connection; }
    public void Do() => _editor.AddConnectionCore(_connection);
    public void Undo() => _editor.RemoveConnectionCore(_connection);
}

/// <summary>Removes a connection; undo restores it.</summary>
internal sealed class DisconnectCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly ConnectionViewModel _connection;
    public DisconnectCommand(BotEditorViewModel editor, ConnectionViewModel connection) { _editor = editor; _connection = connection; }
    public void Do() => _editor.RemoveConnectionCore(_connection);
    public void Undo() => _editor.AddConnectionCore(_connection);
}

/// <summary>Removes a node and its incident connections; undo restores all of them.</summary>
internal sealed class DeleteNodeCommand : IUndoableCommand
{
    private readonly BotEditorViewModel _editor;
    private readonly NodeViewModel _node;
    private readonly IReadOnlyList<ConnectionViewModel> _incident;
    public DeleteNodeCommand(BotEditorViewModel editor, NodeViewModel node, IReadOnlyList<ConnectionViewModel> incident)
    { _editor = editor; _node = node; _incident = incident; }

    public void Do()
    {
        foreach (var c in _incident) { _editor.RemoveConnectionCore(c); }
        _editor.RemoveNodeCore(_node);
    }

    public void Undo()
    {
        _editor.AddNodeCore(_node);
        foreach (var c in _incident) { _editor.AddConnectionCore(c); }
    }
}
```

- [ ] **Step 4: Modify `BotEditorViewModel`**

Replace `BotBuilder.Core/BotEditorViewModel.cs` with:
```csharp
using System.Collections.ObjectModel;
using AdbCore.Actions;
using AdbCore.Serialization;
using BotBuilder.Core.Connections;
using BotBuilder.Core.Palette;
using BotBuilder.Core.Undo;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core;

/// <summary>Root view-model for the editor: nodes, connections, selection, undoable operations.</summary>
public partial class BotEditorViewModel : ObservableObject
{
    private readonly ActionRegistry _registry;
    private readonly BotSerializer _serializer = new();
    private readonly UndoStack _undo = new();

    [ObservableProperty] private string _botName = "Untitled";
    [ObservableProperty] private NodeViewModel? _selectedNode;
    [ObservableProperty] private ConnectionViewModel? _selectedConnection;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _filePath;

    public BotEditorViewModel(ActionRegistry registry)
    {
        _registry = registry;
        Palette = new PaletteViewModel(registry);
        Nodes = new ObservableCollection<NodeViewModel>();
        Connections = new ObservableCollection<ConnectionViewModel>();
        New();
    }

    public ObservableCollection<NodeViewModel> Nodes { get; }
    public ObservableCollection<ConnectionViewModel> Connections { get; }
    public PaletteViewModel Palette { get; }
    public Guid BotId { get; private set; }

    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;

    public NodeViewModel AddNode(string typeKey, double x, double y)
    {
        var definition = _registry.Get(typeKey);
        var node = NodeViewModel.FromDefinition(definition, Guid.NewGuid(), definition.DisplayName, x, y);
        _undo.Execute(new AddNodeCommand(this, node));
        AfterEdit();
        return node;
    }

    /// <summary>Live position update during a drag (not itself undoable; commit with <see cref="CommitMove"/>).</summary>
    public void MoveNode(NodeViewModel node, double x, double y)
    {
        node.X = x;
        node.Y = y;
        IsDirty = true;
    }

    /// <summary>Records a completed drag as a single undoable move.</summary>
    public void CommitMove(NodeViewModel node, double oldX, double oldY)
    {
        if (oldX == node.X && oldY == node.Y)
        {
            return;
        }
        _undo.PushExecuted(new MoveNodeCommand(node, oldX, oldY, node.X, node.Y));
        AfterEdit();
    }

    public ConnectionError Connect(NodeViewModel source, PortViewModel sourcePort, NodeViewModel target, PortViewModel targetPort)
    {
        var error = ConnectionValidator.Validate(Connections, source, sourcePort, target, targetPort);
        if (error != ConnectionError.None)
        {
            return error;
        }
        var connection = new ConnectionViewModel(Guid.NewGuid(), source, sourcePort, target, targetPort);
        _undo.Execute(new ConnectCommand(this, connection));
        AfterEdit();
        return ConnectionError.None;
    }

    public void Disconnect(ConnectionViewModel connection)
    {
        _undo.Execute(new DisconnectCommand(this, connection));
        AfterEdit();
    }

    public void DeleteNode(NodeViewModel node)
    {
        var incident = Connections
            .Where(c => ReferenceEquals(c.Source, node) || ReferenceEquals(c.Target, node))
            .ToList();
        _undo.Execute(new DeleteNodeCommand(this, node, incident));
        AfterEdit();
    }

    public void DeleteSelection()
    {
        if (SelectedConnection is { } connection)
        {
            Disconnect(connection);
            SelectedConnection = null;
        }
        else if (SelectedNode is { } node)
        {
            DeleteNode(node);
            SelectedNode = null;
        }
    }

    public void Select(NodeViewModel? node)
    {
        foreach (var n in Nodes) { n.IsSelected = ReferenceEquals(n, node); }
        ClearConnectionSelection();
        SelectedConnection = null;
        SelectedNode = node;
    }

    public void SelectConnection(ConnectionViewModel? connection)
    {
        foreach (var c in Connections) { c.IsSelected = ReferenceEquals(c, connection); }
        foreach (var n in Nodes) { n.IsSelected = false; }
        SelectedNode = null;
        SelectedConnection = connection;
    }

    public void Undo()
    {
        _undo.Undo();
        AfterEdit();
    }

    public void Redo()
    {
        _undo.Redo();
        AfterEdit();
    }

    public void New()
    {
        BotId = Guid.NewGuid();
        BotName = "Untitled";
        ClearConnectionSelection();
        foreach (var c in Connections) { c.Detach(); }
        Connections.Clear();
        Nodes.Clear();
        SelectedNode = null;
        SelectedConnection = null;
        _undo.Clear();
        FilePath = null;
        IsDirty = false;
        RaiseUndoState();
    }

    public void Open(string path)
    {
        var bot = _serializer.Load(path);
        DocumentMapper.Populate(this, bot, _registry);
        _undo.Clear();
        FilePath = path;
        IsDirty = false;
        RaiseUndoState();
    }

    public void Save(string path)
    {
        _serializer.Save(DocumentMapper.ToBot(this), path);
        FilePath = path;
        IsDirty = false;
    }

    // ---- internal mutation helpers used by commands and the mapper ----

    internal void AddNodeCore(NodeViewModel node) => Nodes.Add(node);
    internal void RemoveNodeCore(NodeViewModel node) => Nodes.Remove(node);
    internal void AddConnectionCore(ConnectionViewModel connection) => Connections.Add(connection);

    internal void RemoveConnectionCore(ConnectionViewModel connection) => Connections.Remove(connection);

    /// <summary>Replaces editor contents during a load (mapper-only).</summary>
    internal void LoadFrom(Guid botId, string botName, IEnumerable<NodeViewModel> nodes, Func<IReadOnlyList<NodeViewModel>, IEnumerable<ConnectionViewModel>> connectionFactory)
    {
        BotId = botId;
        BotName = botName;
        foreach (var c in Connections) { c.Detach(); }
        Connections.Clear();
        Nodes.Clear();
        foreach (var node in nodes) { Nodes.Add(node); }
        foreach (var connection in connectionFactory(Nodes)) { Connections.Add(connection); }
        SelectedNode = null;
        SelectedConnection = null;
    }

    private void AfterEdit()
    {
        IsDirty = true;
        RaiseUndoState();
    }

    private void RaiseUndoState()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void ClearConnectionSelection()
    {
        foreach (var c in Connections) { c.IsSelected = false; }
    }
}
```

- [ ] **Step 5: Modify `DocumentMapper` to persist connections**

Replace `BotBuilder.Core/DocumentMapper.cs` with:
```csharp
using AdbCore.Actions;
using AdbCore.Models;
using BotBuilder.Core.Connections;

namespace BotBuilder.Core;

/// <summary>Maps between the persisted <see cref="Bot"/> model and the editor view-model.</summary>
public static class DocumentMapper
{
    private const string UnknownCategory = "Unknown";

    public static Bot ToBot(BotEditorViewModel editor)
    {
        var bot = new Bot { Id = editor.BotId, Name = editor.BotName };

        foreach (var node in editor.Nodes)
        {
            bot.Actions.Add(new BotAction
            {
                Id = node.Id,
                TypeKey = node.TypeKey,
                Label = node.Label,
                CanvasPosition = new Position { X = node.X, Y = node.Y },
            });
        }

        foreach (var c in editor.Connections)
        {
            bot.Connections.Add(new ActionConnection
            {
                Id = c.Id,
                SourceActionId = c.Source.Id,
                SourcePort = c.SourcePort.Name,
                TargetActionId = c.Target.Id,
                TargetPort = c.TargetPort.Name,
            });
        }

        return bot;
    }

    public static void Populate(BotEditorViewModel editor, Bot bot, ActionRegistry registry)
    {
        var nodes = bot.Actions.Select(a => BuildNode(a, registry)).ToList();

        editor.LoadFrom(bot.Id, bot.Name, nodes, placedNodes => BuildConnections(bot, placedNodes));
    }

    private static IEnumerable<ConnectionViewModel> BuildConnections(Bot bot, IReadOnlyList<NodeViewModel> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Id);

        foreach (var c in bot.Connections)
        {
            if (!byId.TryGetValue(c.SourceActionId, out var source) ||
                !byId.TryGetValue(c.TargetActionId, out var target))
            {
                continue; // skip dangling connections
            }

            var sourcePort = source.OutputPorts.FirstOrDefault(p => p.Name == c.SourcePort);
            var targetPort = target.InputPorts.FirstOrDefault(p => p.Name == c.TargetPort);
            if (sourcePort is null || targetPort is null)
            {
                continue;
            }

            yield return new ConnectionViewModel(c.Id, source, sourcePort, target, targetPort);
        }
    }

    private static NodeViewModel BuildNode(BotAction action, ActionRegistry registry)
    {
        if (registry.TryGet(action.TypeKey, out var definition) && definition is not null)
        {
            return NodeViewModel.FromDefinition(
                definition, action.Id, action.Label, action.CanvasPosition.X, action.CanvasPosition.Y);
        }

        return new NodeViewModel(
            action.Id, action.TypeKey, action.Label, UnknownCategory,
            Array.Empty<PortViewModel>(), Array.Empty<PortViewModel>(),
            action.CanvasPosition.X, action.CanvasPosition.Y);
    }
}
```

Note: the M3a `DocumentMapperTests` call `editor.LoadFrom` indirectly via `Populate` — its signature changed, but those tests only call `Populate`, so they keep working. The old `LoadFrom(Guid, string, IEnumerable<NodeViewModel>)` overload is replaced by the new one; no other caller uses it.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests pass (92 prior + 8 new = 100), 0 failures. The M3a editor/mapper tests still pass (AddNode still adds + dirty; Save/Open round-trip still works, now also carrying connections).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(builder): undoable connect/disconnect/delete/move and connection persistence"
```

---

### Task 6: WPF — render connections + revised card port layout

> **Verification:** clean `dotnet build` (0 warnings) + `dotnet test` (100) + Manual Verification Checklist.

**Files:**
- Modify: `BotBuilder/MainWindow.xaml`

- [ ] **Step 1: Update the canvas to draw connections and place ports by anchor**

In `BotBuilder/MainWindow.xaml`, replace the **canvas `Border` (Grid.Column="1")** contents so the canvas hosts a connection layer beneath the node layer, and the node card places its ports at the shared anchor offsets. Replace the entire `<Border Grid.Column="1" ...> ... </Border>` element with:
```xml
            <Border Grid.Column="1" Background="#FAFAFA" BorderBrush="#CCC" BorderThickness="1,0,1,0">
                <Grid x:Name="CanvasRoot" Background="Transparent">
                    <!-- Connection layer (under the nodes) -->
                    <ItemsControl ItemsSource="{Binding Connections}" IsHitTestVisible="True">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate><Canvas /></ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Canvas>
                                    <!-- transparent thick hit-area -->
                                    <Path Data="{Binding PathData}" Stroke="Transparent" StrokeThickness="10"
                                          MouseLeftButtonDown="Connection_MouseLeftButtonDown" />
                                    <!-- visible curve -->
                                    <Path Data="{Binding PathData}" StrokeThickness="2" IsHitTestVisible="False"
                                          Stroke="{Binding IsSelected, Converter={x:Static local:SelectionBorderConverter.Instance}}" />
                                </Canvas>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <!-- Node layer -->
                    <ItemsControl x:Name="NodeHost" ItemsSource="{Binding Nodes}"
                                  AllowDrop="True" Drop="Canvas_Drop" Background="Transparent">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate><Canvas Background="Transparent" /></ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemContainerStyle>
                            <Style TargetType="ContentPresenter">
                                <Setter Property="Canvas.Left" Value="{Binding X}" />
                                <Setter Property="Canvas.Top" Value="{Binding Y}" />
                            </Style>
                        </ItemsControl.ItemContainerStyle>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Width="160" MinHeight="70" CornerRadius="6" Background="White"
                                        BorderBrush="{Binding IsSelected, Converter={x:Static local:SelectionBorderConverter.Instance}}"
                                        BorderThickness="2"
                                        MouseLeftButtonDown="Node_MouseLeftButtonDown">
                                    <Grid>
                                        <StackPanel>
                                            <Border Background="{Binding CategoryColor, Converter={StaticResource CategoryBrush}}"
                                                    CornerRadius="6,6,0,0" Padding="6,3" MinHeight="28">
                                                <TextBlock Text="{Binding Label}" Foreground="White" FontWeight="Bold" />
                                            </Border>
                                            <TextBlock Text="{Binding TargetBadge}" FontSize="10" Foreground="#888" Margin="6,2"
                                                       Visibility="{Binding TargetBadge, Converter={x:Static local:NullToCollapsedConverter.Instance}}" />
                                        </StackPanel>
                                        <!-- ports positioned at shared anchor offsets -->
                                        <ItemsControl ItemsSource="{Binding InputPorts}">
                                            <ItemsControl.ItemsPanel><ItemsPanelTemplate><Canvas /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <Ellipse Width="10" Height="10" Fill="#555" ToolTip="{Binding Name}"
                                                             Canvas.Left="{Binding AnchorOffset.X}" Canvas.Top="{Binding AnchorOffset.Y}"
                                                             RenderTransform="{x:Static local:PortCenteringTransform.Instance}"
                                                             MouseLeftButtonDown="InputPort_MouseLeftButtonDown" />
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                        <ItemsControl ItemsSource="{Binding OutputPorts}">
                                            <ItemsControl.ItemsPanel><ItemsPanelTemplate><Canvas /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <Ellipse Width="10" Height="10" Fill="#555" ToolTip="{Binding Name}"
                                                             Canvas.Left="{Binding AnchorOffset.X}" Canvas.Top="{Binding AnchorOffset.Y}"
                                                             RenderTransform="{x:Static local:PortCenteringTransform.Instance}"
                                                             MouseLeftButtonDown="OutputPort_MouseLeftButtonDown" />
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </Grid>
            </Border>
```

- [ ] **Step 2: Add the port-centering transform helper**

Append to `BotBuilder/ValueConverters.cs`:
```csharp
/// <summary>Shifts a 10px port ellipse so its centre sits on the anchor point.</summary>
public static class PortCenteringTransform
{
    public static readonly System.Windows.Media.TranslateTransform Instance = new(-5, -5);
}
```

- [ ] **Step 3: Add the gesture handler stubs so the XAML compiles** (real logic in Task 7)

In `BotBuilder/MainWindow.xaml.cs`, add these stub methods to the class (alongside the existing handlers):
```csharp
    private void Connection_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
    private void InputPort_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
    private void OutputPort_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
```

- [ ] **Step 4: Build and test**

Run: `dotnet build ADB.slnx`
Expected: 0 warnings, 0 errors.
Run: `dotnet test`
Expected: 100 tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): render connection bezier layer and anchor-positioned ports"
```

**Manual Verification Checklist (user runs `dotnet run --project BotBuilder`):**
- Node cards show input ports on the left edge and output ports on the right edge.
- (Connecting is wired in Task 7; for now, loading a `.bot` that already has connections — e.g. one saved by the M3b tests — should draw curves between the right port of the source and the left port of the target.)

---

### Task 7: WPF — connect, delete, and undo/redo gestures

> **Verification:** clean `dotnet build` (0 warnings) + `dotnet test` (100) + Manual Verification Checklist.

**Files:**
- Modify: `BotBuilder/MainWindow.xaml` (add Edit menu)
- Modify: `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Add the Edit menu**

In `BotBuilder/MainWindow.xaml`, add an Edit menu right after the File `MenuItem` (inside the `<Menu>`):
```xml
            <MenuItem Header="_Edit">
                <MenuItem Header="_Undo" Click="Undo_Click" InputGestureText="Ctrl+Z" />
                <MenuItem Header="_Redo" Click="Redo_Click" InputGestureText="Ctrl+Y" />
                <Separator />
                <MenuItem Header="_Delete" Click="Delete_Click" InputGestureText="Del" />
            </MenuItem>
```
Also add key bindings: inside the root `<Window ...>` element, after the `</Window.Resources>` close tag, add:
```xml
    <Window.InputBindings>
        <KeyBinding Key="Z" Modifiers="Control" Command="{x:Null}" />
    </Window.InputBindings>
```
(Leave the actual key handling to the `Window`-level `KeyDown` wired in code-behind below — the binding element above is a placeholder removed in favour of code-behind; if it causes a build warning, omit it.) Instead, set `KeyDown` on the Window: change the opening tag to include `KeyDown="Window_KeyDown"` and `Focusable="True"`.

So the opening `<Window ...>` tag gains: `KeyDown="Window_KeyDown"`.

- [ ] **Step 2: Implement the handlers**

In `BotBuilder/MainWindow.xaml.cs`, replace the three stub methods from Task 6 and add the menu/key handlers + connection-drag state. The full additions (place inside the class; keep all existing M3a handlers and fields):

```csharp
    // ---- connection drag state ----
    private NodeViewModel? _connectSourceNode;
    private PortViewModel? _connectSourcePort;

    private void Undo_Click(object sender, RoutedEventArgs e) => _editor.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _editor.Redo();
    private void Delete_Click(object sender, RoutedEventArgs e) => _editor.DeleteSelection();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            _editor.DeleteSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _editor.Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _editor.Redo();
            e.Handled = true;
        }
    }

    private void Connection_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ConnectionViewModel connection })
        {
            _editor.SelectConnection(connection);
            e.Handled = true;
        }
    }

    private void OutputPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PortViewModel port } fe
            && NodeOf(fe) is { } node)
        {
            _connectSourceNode = node;
            _connectSourcePort = port;
            ((UIElement)sender).CaptureMouse();
            NodeHost.MouseLeftButtonUp += FinishConnectionDrag;
            e.Handled = true;
        }
    }

    private void InputPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // starting from an input port is not supported; ignore (drag starts at outputs)
    }

    private void FinishConnectionDrag(object sender, MouseButtonEventArgs e)
    {
        NodeHost.MouseLeftButtonUp -= FinishConnectionDrag;
        Mouse.Capture(null);

        var source = _connectSourceNode;
        var sourcePort = _connectSourcePort;
        _connectSourceNode = null;
        _connectSourcePort = null;
        if (source is null || sourcePort is null)
        {
            return;
        }

        // hit-test the element under the pointer for an input port
        var hit = Mouse.DirectlyOver as FrameworkElement;
        while (hit is not null)
        {
            if (hit.DataContext is PortViewModel { Direction: PortDirection.In } targetPort
                && NodeOf(hit) is { } targetNode)
            {
                _editor.Connect(source, sourcePort, targetNode, targetPort);
                return;
            }
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit) as FrameworkElement;
        }
    }

    private static NodeViewModel? NodeOf(DependencyObject start)
    {
        var current = start;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: NodeViewModel node })
            {
                return node;
            }
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
```

Also add `using BotBuilder.Core.Connections;` to the using block.

Note on the node-vs-port mouse-down: a port `Ellipse` sits on top of the card `Border`. `OutputPort_MouseLeftButtonDown` sets `e.Handled = true`, so the card's `Node_MouseLeftButtonDown` does not also fire — port drags and node drags don't conflict.

- [ ] **Step 3: Build and test**

Run: `dotnet build ADB.slnx`
Expected: 0 warnings, 0 errors. (If the placeholder `<Window.InputBindings>` from Step 1 produced a warning or error, remove that element — the `KeyDown` handler is the real mechanism.)
Run: `dotnet test`
Expected: 100 tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(builder): connect by port drag, delete, and undo/redo gestures"
```

**Manual Verification Checklist (user runs `dotnet run --project BotBuilder`):**
- Drag from a node's right-edge (output) port to another node's left-edge (input) port creates a curved connection. Invalid drops (output→output, self, would-be cycle, duplicate) silently do nothing.
- Moving a connected node makes its connections follow.
- Click a connection curve to select it (highlights); Delete removes it.
- Select a node and press Delete: the node and any connections touching it disappear.
- Ctrl+Z undoes the last add / move / connect / disconnect / delete; Ctrl+Y redoes. Edit menu items do the same.
- Save a connected graph, New, Open it back: nodes and connections reappear.

---

## Self-Review

**Spec coverage (M3b slice, spec §3.2/§3.3):**
- Port-to-port connections + validation (output→input, self, dup, cycle) — Task 3 (`ConnectionValidator`) + Task 5 (`Connect`) + Task 7 (gesture). ✓
- `ConnectionViewModel` + bezier geometry — Task 2. ✓
- Connection rendering — Task 6. ✓
- Disconnect (click + Delete) — Task 5 (`Disconnect`/`DeleteSelection`) + Task 7. ✓
- Node delete (cascades connections) — Task 5 (`DeleteNode`). ✓
- Undo/redo across all ops — Task 4 (`UndoStack`) + Task 5 (all ops go through it) + Task 7 (Ctrl+Z/Y). ✓
- Connection persistence in `.bot` — Task 5 (`DocumentMapper`). ✓

**Placeholder scan:** Task 6 adds 3 handler stubs explicitly completed in Task 7 (and notes the `<Window.InputBindings>` placeholder to drop if it warns). No other placeholders.

**Type consistency:** `CanvasPoint`, `NodeLayout.Input/OutputAnchor`, `PortViewModel.AnchorOffset`, `ConnectionGeometry.BuildPath/ControlPoints`, `ConnectionViewModel(Id,Source,SourcePort,Target,TargetPort)`+`PathData`+`Detach`, `ConnectionError`, `ConnectionValidator.Validate(existing,src,srcPort,tgt,tgtPort)`, `UndoStack.Execute/PushExecuted/Undo/Redo/Clear/CanUndo/CanRedo`, `IUndoableCommand.Do/Undo`, editor `Connect/Disconnect/DeleteNode/DeleteSelection/CommitMove/Select/SelectConnection/Undo/Redo/Connections/SelectedConnection/CanUndo/CanRedo` + internal `AddNodeCore/RemoveNodeCore/AddConnectionCore/RemoveConnectionCore/LoadFrom` — names are consistent across tasks and the XAML bindings (`Connections`, `PathData`, `IsSelected`, `InputPorts/OutputPorts`, `AnchorOffset.X/Y`). The `LoadFrom` signature change is matched between `BotEditorViewModel` (Task 5) and `DocumentMapper` (Task 5). ✓

**Scope:** No pan/zoom, no marquee, no Target Bar, no Properties form — those remain M3c/M4. ✓
