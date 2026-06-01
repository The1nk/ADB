# M3a — Builder Canvas (Shell + Palette + Cards + Add/Move/Select + File I/O) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the Bot Builder WPF app's first usable slice: a tested view-model core (`BotBuilder.Core`) plus a thin WPF shell that shows a searchable action palette, renders nodes as data-templated cards on a canvas, supports adding (palette double-click + drag-drop) / moving (drag) / selecting (click) nodes, and does New/Open/Save of `.bot` files.

**Architecture:** `BotBuilder.Core` (class lib, `net10.0-windows`, **no `UseWPF`**, refs `AdbCore` + `CommunityToolkit.Mvvm`) holds all logic as unit-tested view-models. `BotBuilder` (WPF exe) is a thin shell: XAML views bind to the core, and minimal code-behind translates input gestures into core calls. Per the approved spec `Docs/Specs/2026-06-01-m3-builder-canvas-design.md`.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm, xUnit. Builds on merged M1+M2.

---

## Verification model

- **Tasks 1–4 (`BotBuilder.Core`)**: strict TDD, fully verified by `dotnet test`.
- **Tasks 5–6 (`BotBuilder` WPF)**: cannot be unit-tested headlessly. Verification = `dotnet build ADB.slnx` succeeds with **0 warnings** AND `dotnet test` still green (no regressions). Each WPF task ends with a **Manual Verification Checklist** for the user to confirm by running `BotBuilder.exe`.

## File Structure

```
BotBuilder.Core/
  BotBuilder.Core.csproj
  CategoryColors.cs           # category name -> hex colour
  PortDirection.cs            # In | Out
  PortViewModel.cs            # a port on a node card
  NodeViewModel.cs            # a node card VM (wraps an action), FromDefinition factory
  Palette/
    PaletteItem.cs            # one draggable palette entry
    PaletteCategory.cs        # a named group of palette items
    PaletteViewModel.cs       # grouped + searchable, built from an ActionRegistry
  BotEditorViewModel.cs       # root editor VM: nodes, selection, add/move/select, new/open/save
  DocumentMapper.cs           # Bot <-> editor VM mapping
BotBuilder/
  BotBuilder.csproj
  App.xaml / App.xaml.cs
  MainWindow.xaml / MainWindow.xaml.cs
  CategoryColorToBrushConverter.cs
BotBuilder.Core.Tests/
  BotBuilder.Core.Tests.csproj
  CategoryColorsTests.cs
  NodeViewModelTests.cs
  PaletteViewModelTests.cs
  BotEditorViewModelTests.cs
  DocumentMapperTests.cs
```

---

### Task 1: `BotBuilder.Core` scaffold + `CategoryColors`

**Files:**
- Create: `BotBuilder.Core/BotBuilder.Core.csproj`, `BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj`
- Create: `BotBuilder.Core/CategoryColors.cs`
- Test: `BotBuilder.Core.Tests/CategoryColorsTests.cs`
- Modify: `ADB.slnx`

- [ ] **Step 1: Scaffold projects**

From the worktree root:
```bash
dotnet new classlib -o BotBuilder.Core
dotnet new xunit -o BotBuilder.Core.Tests
dotnet sln ADB.slnx add BotBuilder.Core/BotBuilder.Core.csproj BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj
dotnet add BotBuilder.Core/BotBuilder.Core.csproj reference AdbCore/AdbCore.csproj
dotnet add BotBuilder.Core/BotBuilder.Core.csproj package CommunityToolkit.Mvvm
dotnet add BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj reference BotBuilder.Core/BotBuilder.Core.csproj
dotnet add BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj reference AdbCore/AdbCore.csproj
```

Overwrite `BotBuilder.Core/BotBuilder.Core.csproj` so the PropertyGroup is exactly (keep the generated CommunityToolkit.Mvvm PackageReference and the AdbCore ProjectReference ItemGroups):
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>BotBuilder.Core</RootNamespace>
  </PropertyGroup>

  <!-- Keep: <PackageReference Include="CommunityToolkit.Mvvm" ... /> -->
  <!-- Keep: <ProjectReference Include="..\AdbCore\AdbCore.csproj" /> -->

</Project>
```
Note: NO `<UseWPF>` — this library must stay WPF-free.

In `BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj`, set `<TargetFramework>net10.0-windows</TargetFramework>` (leave template package versions; keep both ProjectReferences). Delete the template files `BotBuilder.Core/Class1.cs` and `BotBuilder.Core.Tests/UnitTest1.cs`.

- [ ] **Step 2: Write the failing test**

Create `BotBuilder.Core.Tests/CategoryColorsTests.cs`:
```csharp
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class CategoryColorsTests
{
    [Fact]
    public void ColorFor_KnownCategories_ReturnsDistinctHex()
    {
        var control = CategoryColors.ColorFor("Control Flow");
        var data = CategoryColors.ColorFor("Data");

        Assert.StartsWith("#", control);
        Assert.StartsWith("#", data);
        Assert.NotEqual(control, data);
    }

    [Fact]
    public void ColorFor_IsCaseInsensitive()
    {
        Assert.Equal(CategoryColors.ColorFor("Control Flow"), CategoryColors.ColorFor("control flow"));
    }

    [Fact]
    public void ColorFor_UnknownCategory_ReturnsDefault()
    {
        Assert.Equal(CategoryColors.Default, CategoryColors.ColorFor("Nonexistent"));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `CategoryColors` does not exist.

- [ ] **Step 4: Implement `CategoryColors`**

Create `BotBuilder.Core/CategoryColors.cs`:
```csharp
namespace BotBuilder.Core;

/// <summary>Maps an action category to the hex colour used for its node-card header.</summary>
public static class CategoryColors
{
    /// <summary>Colour used for categories with no explicit mapping.</summary>
    public const string Default = "#9B9B9B";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Control Flow"] = "#4A90D9",
        ["Screen"] = "#7ED321",
        ["Input"] = "#F5A623",
        ["Android"] = "#9013FE",
        ["Browser"] = "#50E3C2",
        ["Web & API"] = "#B8E986",
        ["Files & System"] = "#BD10E0",
        ["Desktop UI"] = "#417505",
        ["Data"] = "#D0021B",
        ["Scripting"] = "#8B572A",
    };

    public static string ColorFor(string category)
        => Map.TryGetValue(category, out var hex) ? hex : Default;
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test`
Expected: all tests pass (54 prior across AdbCore/BotRunner + 3 new = 57), 0 failures.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(builder): scaffold BotBuilder.Core with category colour map"
```

---

### Task 2: `PortViewModel` + `NodeViewModel`

**Files:**
- Create: `BotBuilder.Core/PortDirection.cs`, `PortViewModel.cs`, `NodeViewModel.cs`
- Test: `BotBuilder.Core.Tests/NodeViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/NodeViewModelTests.cs`:
```csharp
using System.ComponentModel;
using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeViewModelTests
{
    [Fact]
    public void FromDefinition_DerivesTypeKeyCategoryAndPorts()
    {
        var node = NodeViewModel.FromDefinition(new LogAction(), Guid.NewGuid(), label: "", x: 10, y: 20);

        Assert.Equal("data.log", node.TypeKey);
        Assert.Equal("Data", node.Category);
        Assert.Equal("Log", node.Label); // empty label falls back to DisplayName
        Assert.Single(node.InputPorts);
        Assert.Equal(PortDirection.In, node.InputPorts[0].Direction);
        Assert.Single(node.OutputPorts);
        Assert.Equal(PortDirection.Out, node.OutputPorts[0].Direction);
        Assert.Equal(10, node.X);
        Assert.Equal(20, node.Y);
    }

    [Fact]
    public void FromDefinition_KeepsExplicitLabel()
    {
        var node = NodeViewModel.FromDefinition(new StartAction(), Guid.NewGuid(), label: "Begin", x: 0, y: 0);

        Assert.Equal("Begin", node.Label);
        Assert.Empty(node.InputPorts);   // Start has no input ports
        Assert.Single(node.OutputPorts);
    }

    [Fact]
    public void CategoryColor_MatchesCategoryColors()
    {
        var node = NodeViewModel.FromDefinition(new StartAction(), Guid.NewGuid(), "", 0, 0);

        Assert.Equal(CategoryColors.ColorFor("Control Flow"), node.CategoryColor);
    }

    [Fact]
    public void SettingX_RaisesPropertyChanged()
    {
        var node = NodeViewModel.FromDefinition(new StartAction(), Guid.NewGuid(), "", 0, 0);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        node.X = 99;

        Assert.Contains(nameof(NodeViewModel.X), raised);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `NodeViewModel`, `PortViewModel`, `PortDirection` don't exist.

- [ ] **Step 3: Implement the types**

Create `BotBuilder.Core/PortDirection.cs`:
```csharp
namespace BotBuilder.Core;

/// <summary>Whether a port accepts incoming flow or emits outgoing flow.</summary>
public enum PortDirection
{
    In,
    Out,
}
```

Create `BotBuilder.Core/PortViewModel.cs`:
```csharp
namespace BotBuilder.Core;

/// <summary>A single input or output port shown on a node card.</summary>
public sealed class PortViewModel
{
    public PortViewModel(string name, PortDirection direction)
    {
        Name = name;
        Direction = direction;
    }

    public string Name { get; }
    public PortDirection Direction { get; }
}
```

Create `BotBuilder.Core/NodeViewModel.cs`:
```csharp
using AdbCore.Actions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core;

/// <summary>A node card on the canvas, wrapping a bot action instance.</summary>
public partial class NodeViewModel : ObservableObject
{
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _targetBadge;

    public NodeViewModel(
        Guid id,
        string typeKey,
        string label,
        string category,
        IReadOnlyList<PortViewModel> inputPorts,
        IReadOnlyList<PortViewModel> outputPorts,
        double x,
        double y)
    {
        Id = id;
        TypeKey = typeKey;
        _label = label;
        Category = category;
        InputPorts = inputPorts;
        OutputPorts = outputPorts;
        _x = x;
        _y = y;
    }

    public Guid Id { get; }
    public string TypeKey { get; }
    public string Category { get; }
    public string CategoryColor => CategoryColors.ColorFor(Category);
    public IReadOnlyList<PortViewModel> InputPorts { get; }
    public IReadOnlyList<PortViewModel> OutputPorts { get; }

    /// <summary>Builds a node from an action definition, deriving ports/category from it.</summary>
    public static NodeViewModel FromDefinition(IActionDefinition definition, Guid id, string label, double x, double y)
        => new(
            id,
            definition.TypeKey,
            string.IsNullOrEmpty(label) ? definition.DisplayName : label,
            definition.Category,
            definition.InputPorts.Select(p => new PortViewModel(p.Name, PortDirection.In)).ToList(),
            definition.OutputPorts.Select(p => new PortViewModel(p.Name, PortDirection.Out)).ToList(),
            x,
            y);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests pass (57 prior + 4 new = 61), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): add node and port view-models"
```

---

### Task 3: `PaletteViewModel`

**Files:**
- Create: `BotBuilder.Core/Palette/PaletteItem.cs`, `PaletteCategory.cs`, `PaletteViewModel.cs`
- Test: `BotBuilder.Core.Tests/PaletteViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/PaletteViewModelTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core.Palette;
using Xunit;

namespace BotBuilder.Core.Tests;

public class PaletteViewModelTests
{
    private static ActionRegistry SeededRegistry()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return defs;
    }

    [Fact]
    public void Categories_GroupBuiltInsByCategory()
    {
        var palette = new PaletteViewModel(SeededRegistry());

        var control = palette.Categories.Single(c => c.Name == "Control Flow");
        var data = palette.Categories.Single(c => c.Name == "Data");

        Assert.Equal(2, control.Items.Count); // Start + End
        Assert.Single(data.Items);            // Log
    }

    [Fact]
    public void Search_FiltersByDisplayName_CaseInsensitive()
    {
        var palette = new PaletteViewModel(SeededRegistry()) { SearchText = "lo" };

        var allItems = palette.Categories.SelectMany(c => c.Items).ToList();

        Assert.Single(allItems);
        Assert.Equal("data.log", allItems[0].TypeKey);
    }

    [Fact]
    public void Search_DropsEmptyCategories()
    {
        var palette = new PaletteViewModel(SeededRegistry()) { SearchText = "log" };

        Assert.DoesNotContain(palette.Categories, c => c.Name == "Control Flow");
    }

    [Fact]
    public void ClearingSearch_RestoresAll()
    {
        var palette = new PaletteViewModel(SeededRegistry()) { SearchText = "log" };
        palette.SearchText = "";

        Assert.Equal(3, palette.Categories.SelectMany(c => c.Items).Count());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — palette types don't exist.

- [ ] **Step 3: Implement the palette types**

Create `BotBuilder.Core/Palette/PaletteItem.cs`:
```csharp
namespace BotBuilder.Core.Palette;

/// <summary>One draggable entry in the action palette.</summary>
public sealed class PaletteItem
{
    public PaletteItem(string typeKey, string displayName, string category)
    {
        TypeKey = typeKey;
        DisplayName = displayName;
        Category = category;
    }

    public string TypeKey { get; }
    public string DisplayName { get; }
    public string Category { get; }
}
```

Create `BotBuilder.Core/Palette/PaletteCategory.cs`:
```csharp
namespace BotBuilder.Core.Palette;

/// <summary>A named group of palette items.</summary>
public sealed class PaletteCategory
{
    public PaletteCategory(string name, IReadOnlyList<PaletteItem> items)
    {
        Name = name;
        Items = items;
    }

    public string Name { get; }
    public IReadOnlyList<PaletteItem> Items { get; }
}
```

Create `BotBuilder.Core/Palette/PaletteViewModel.cs`:
```csharp
using AdbCore.Actions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Palette;

/// <summary>Searchable, category-grouped view of the registered action definitions.</summary>
public partial class PaletteViewModel : ObservableObject
{
    private readonly ActionRegistry _registry;

    [ObservableProperty] private string _searchText = string.Empty;

    public PaletteViewModel(ActionRegistry registry)
    {
        _registry = registry;
        Categories = new System.Collections.ObjectModel.ObservableCollection<PaletteCategory>();
        Rebuild();
    }

    public System.Collections.ObjectModel.ObservableCollection<PaletteCategory> Categories { get; }

    partial void OnSearchTextChanged(string value) => Rebuild();

    private void Rebuild()
    {
        Categories.Clear();

        var matches = _registry.All
            .Where(d => string.IsNullOrWhiteSpace(SearchText)
                        || d.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var group in matches.GroupBy(d => d.Category).OrderBy(g => g.Key))
        {
            var items = group
                .OrderBy(d => d.DisplayName)
                .Select(d => new PaletteItem(d.TypeKey, d.DisplayName, d.Category))
                .ToList();
            Categories.Add(new PaletteCategory(group.Key, items));
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests pass (61 prior + 4 new = 65), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): add searchable grouped action palette view-model"
```

---

### Task 4: `BotEditorViewModel` + `DocumentMapper`

**Files:**
- Create: `BotBuilder.Core/DocumentMapper.cs`, `BotBuilder.Core/BotEditorViewModel.cs`
- Test: `BotBuilder.Core.Tests/BotEditorViewModelTests.cs`, `DocumentMapperTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/BotEditorViewModelTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BotEditorViewModelTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void AddNode_AddsNodeAndMarksDirty()
    {
        var editor = NewEditor();

        var node = editor.AddNode("control.start", 5, 6);

        Assert.Contains(node, editor.Nodes);
        Assert.Equal("control.start", node.TypeKey);
        Assert.Equal(5, node.X);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void MoveNode_UpdatesPositionAndDirty()
    {
        var editor = NewEditor();
        var node = editor.AddNode("control.start", 0, 0);

        editor.MoveNode(node, 40, 50);

        Assert.Equal(40, node.X);
        Assert.Equal(50, node.Y);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void Select_SetsIsSelectedExclusively()
    {
        var editor = NewEditor();
        var a = editor.AddNode("control.start", 0, 0);
        var b = editor.AddNode("control.end", 0, 0);

        editor.Select(a);
        Assert.True(a.IsSelected);
        Assert.False(b.IsSelected);
        Assert.Same(a, editor.SelectedNode);

        editor.Select(b);
        Assert.False(a.IsSelected);
        Assert.True(b.IsSelected);
    }

    [Fact]
    public void New_ClearsNodesAndDirty()
    {
        var editor = NewEditor();
        editor.AddNode("control.start", 0, 0);

        editor.New();

        Assert.Empty(editor.Nodes);
        Assert.False(editor.IsDirty);
        Assert.Null(editor.SelectedNode);
    }

    [Fact]
    public void SaveThenOpen_RestoresNodes()
    {
        var editor = NewEditor();
        editor.BotName = "RoundTrip";
        var start = editor.AddNode("control.start", 10, 20);
        editor.AddNode("data.log", 100, 60);
        var path = Path.Combine(Path.GetTempPath(), $"adb-m3a-{Guid.NewGuid():N}.bot");

        try
        {
            editor.Save(path);
            Assert.False(editor.IsDirty);

            var reopened = NewEditor();
            reopened.Open(path);

            Assert.Equal("RoundTrip", reopened.BotName);
            Assert.Equal(2, reopened.Nodes.Count);
            var startAgain = reopened.Nodes.Single(n => n.TypeKey == "control.start");
            Assert.Equal(10, startAgain.X);
            Assert.Equal(20, startAgain.Y);
            Assert.False(reopened.IsDirty);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
```

Create `BotBuilder.Core.Tests/DocumentMapperTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class DocumentMapperTests
{
    private static ActionRegistry SeededRegistry()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return defs;
    }

    [Fact]
    public void ToBot_ProjectsNodesToActions()
    {
        var registry = SeededRegistry();
        var editor = new BotEditorViewModel(registry) { BotName = "Demo" };
        editor.AddNode("control.start", 7, 8);

        var bot = DocumentMapper.ToBot(editor);

        Assert.Equal("Demo", bot.Name);
        Assert.Equal(editor.BotId, bot.Id);
        var action = Assert.Single(bot.Actions);
        Assert.Equal("control.start", action.TypeKey);
        Assert.Equal(7, action.CanvasPosition.X);
        Assert.Equal(8, action.CanvasPosition.Y);
    }

    [Fact]
    public void Populate_BuildsNodesFromBot_UsingRegistryForPorts()
    {
        var registry = SeededRegistry();
        var bot = new Bot { Name = "Loaded" };
        bot.Actions.Add(new BotAction
        {
            Id = Guid.NewGuid(),
            TypeKey = "data.log",
            Label = "Say hi",
            CanvasPosition = new Position { X = 3, Y = 4 },
        });

        var editor = new BotEditorViewModel(registry);
        DocumentMapper.Populate(editor, bot, registry);

        Assert.Equal("Loaded", editor.BotName);
        var node = Assert.Single(editor.Nodes);
        Assert.Equal("data.log", node.TypeKey);
        Assert.Equal("Say hi", node.Label);
        Assert.Equal(3, node.X);
        Assert.Single(node.InputPorts);  // ports come from the LogAction definition
        Assert.Single(node.OutputPorts);
    }

    [Fact]
    public void Populate_UnknownTypeKey_CreatesNodeWithNoPortsAndDefaultCategory()
    {
        var registry = SeededRegistry();
        var bot = new Bot();
        bot.Actions.Add(new BotAction { Id = Guid.NewGuid(), TypeKey = "ghost.unknown", Label = "Ghost" });

        var editor = new BotEditorViewModel(registry);
        DocumentMapper.Populate(editor, bot, registry);

        var node = Assert.Single(editor.Nodes);
        Assert.Equal("ghost.unknown", node.TypeKey);
        Assert.Empty(node.InputPorts);
        Assert.Empty(node.OutputPorts);
        Assert.Equal("Unknown", node.Category);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `BotEditorViewModel`, `DocumentMapper` don't exist.

- [ ] **Step 3: Implement `DocumentMapper`**

Create `BotBuilder.Core/DocumentMapper.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Models;

namespace BotBuilder.Core;

/// <summary>Maps between the persisted <see cref="Bot"/> model and the editor view-model.</summary>
public static class DocumentMapper
{
    private const string UnknownCategory = "Unknown";

    /// <summary>Assembles a <see cref="Bot"/> from the current editor state (M3a: nodes only).</summary>
    public static Bot ToBot(BotEditorViewModel editor)
    {
        var bot = new Bot
        {
            Id = editor.BotId,
            Name = editor.BotName,
        };

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

        return bot;
    }

    /// <summary>Replaces the editor's contents with nodes built from <paramref name="bot"/>.</summary>
    public static void Populate(BotEditorViewModel editor, Bot bot, ActionRegistry registry)
    {
        editor.LoadFrom(bot.Id, bot.Name, bot.Actions.Select(a => BuildNode(a, registry)));
    }

    private static NodeViewModel BuildNode(BotAction action, ActionRegistry registry)
    {
        if (registry.TryGet(action.TypeKey, out var definition) && definition is not null)
        {
            return NodeViewModel.FromDefinition(
                definition, action.Id, action.Label, action.CanvasPosition.X, action.CanvasPosition.Y);
        }

        return new NodeViewModel(
            action.Id,
            action.TypeKey,
            action.Label,
            UnknownCategory,
            Array.Empty<PortViewModel>(),
            Array.Empty<PortViewModel>(),
            action.CanvasPosition.X,
            action.CanvasPosition.Y);
    }
}
```

- [ ] **Step 4: Implement `BotEditorViewModel`**

Create `BotBuilder.Core/BotEditorViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using AdbCore.Actions;
using AdbCore.Serialization;
using BotBuilder.Core.Palette;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core;

/// <summary>Root view-model for the editor: nodes, selection, and document operations.</summary>
public partial class BotEditorViewModel : ObservableObject
{
    private readonly ActionRegistry _registry;
    private readonly BotSerializer _serializer = new();

    [ObservableProperty] private string _botName = "Untitled";
    [ObservableProperty] private NodeViewModel? _selectedNode;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _filePath;

    public BotEditorViewModel(ActionRegistry registry)
    {
        _registry = registry;
        Palette = new PaletteViewModel(registry);
        Nodes = new ObservableCollection<NodeViewModel>();
        New();
    }

    public ObservableCollection<NodeViewModel> Nodes { get; }
    public PaletteViewModel Palette { get; }
    public Guid BotId { get; private set; }

    public NodeViewModel AddNode(string typeKey, double x, double y)
    {
        var definition = _registry.Get(typeKey);
        var node = NodeViewModel.FromDefinition(definition, Guid.NewGuid(), definition.DisplayName, x, y);
        Nodes.Add(node);
        IsDirty = true;
        return node;
    }

    public void MoveNode(NodeViewModel node, double x, double y)
    {
        node.X = x;
        node.Y = y;
        IsDirty = true;
    }

    public void Select(NodeViewModel? node)
    {
        foreach (var n in Nodes)
        {
            n.IsSelected = ReferenceEquals(n, node);
        }
        SelectedNode = node;
    }

    public void New()
    {
        BotId = Guid.NewGuid();
        BotName = "Untitled";
        Nodes.Clear();
        SelectedNode = null;
        FilePath = null;
        IsDirty = false;
    }

    public void Open(string path)
    {
        var bot = _serializer.Load(path);
        DocumentMapper.Populate(this, bot, _registry);
        FilePath = path;
        IsDirty = false;
    }

    public void Save(string path)
    {
        _serializer.Save(DocumentMapper.ToBot(this), path);
        FilePath = path;
        IsDirty = false;
    }

    /// <summary>Used by <see cref="DocumentMapper"/> to replace editor contents during a load.</summary>
    internal void LoadFrom(Guid botId, string botName, IEnumerable<NodeViewModel> nodes)
    {
        BotId = botId;
        BotName = botName;
        Nodes.Clear();
        foreach (var node in nodes)
        {
            Nodes.Add(node);
        }
        SelectedNode = null;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests pass (65 prior + 8 new = 73), 0 failures.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(builder): add editor view-model and bot/document mapper"
```

---

### Task 5: `BotBuilder` WPF shell — layout, palette, node cards, canvas

> **Verification:** WPF UI is not unit-tested. This task is verified by a clean `dotnet build ADB.slnx` (0 warnings) and `dotnet test` still green, plus the Manual Verification Checklist at the end (for the user).

**Files:**
- Create: `BotBuilder/BotBuilder.csproj`, `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `CategoryColorToBrushConverter.cs`
- Modify: `ADB.slnx`

- [ ] **Step 1: Scaffold the WPF project**

From the worktree root:
```bash
dotnet new wpf -o BotBuilder
dotnet sln ADB.slnx add BotBuilder/BotBuilder.csproj
dotnet add BotBuilder/BotBuilder.csproj reference BotBuilder.Core/BotBuilder.Core.csproj
dotnet add BotBuilder/BotBuilder.csproj reference AdbCore/AdbCore.csproj
```

Ensure `BotBuilder/BotBuilder.csproj` PropertyGroup contains (the `dotnet new wpf` template sets most of this; adjust to match):
```xml
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <RootNamespace>BotBuilder</RootNamespace>
  </PropertyGroup>
```

- [ ] **Step 2: Implement the colour converter**

Create `BotBuilder/CategoryColorToBrushConverter.cs`:
```csharp
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BotBuilder;

/// <summary>Converts a hex colour string (e.g. "#4A90D9") to a <see cref="SolidColorBrush"/>.</summary>
public sealed class CategoryColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value as string ?? "#9B9B9B";
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 3: Implement `App.xaml` / `App.xaml.cs`**

Overwrite `BotBuilder/App.xaml`:
```xml
<Application x:Class="BotBuilder.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
```

Overwrite `BotBuilder/App.xaml.cs`:
```csharp
using System.Windows;

namespace BotBuilder;

public partial class App : Application
{
}
```

- [ ] **Step 4: Implement `MainWindow.xaml` (layout + palette + canvas + node card template)**

Overwrite `BotBuilder/MainWindow.xaml`:
```xml
<Window x:Class="BotBuilder.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:BotBuilder"
        Title="ADB Bot Builder" Height="720" Width="1100">
    <Window.Resources>
        <local:CategoryColorToBrushConverter x:Key="CategoryBrush" />
    </Window.Resources>
    <DockPanel>
        <Menu DockPanel.Dock="Top">
            <MenuItem Header="_File">
                <MenuItem Header="_New" Click="New_Click" />
                <MenuItem Header="_Open..." Click="Open_Click" />
                <MenuItem Header="_Save..." Click="Save_Click" />
            </MenuItem>
        </Menu>

        <Border DockPanel.Dock="Top" Background="#EEE" Padding="6" BorderBrush="#CCC" BorderThickness="0,0,0,1">
            <TextBlock Text="Targets: (Target Bar arrives in M3c)" Foreground="#666" />
        </Border>

        <StatusBar DockPanel.Dock="Bottom">
            <StatusBarItem><TextBlock Text="{Binding BotName}" /></StatusBarItem>
            <StatusBarItem><TextBlock Text="{Binding Nodes.Count, StringFormat='Nodes: {0}'}" /></StatusBarItem>
        </StatusBar>

        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="220" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="240" />
            </Grid.ColumnDefinitions>

            <!-- Palette -->
            <DockPanel Grid.Column="0" Background="#F7F7F7">
                <TextBox DockPanel.Dock="Top" Margin="6"
                         Text="{Binding Palette.SearchText, UpdateSourceTrigger=PropertyChanged}" />
                <ItemsControl ItemsSource="{Binding Palette.Categories}" Margin="6,0">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Margin="0,4">
                                <TextBlock Text="{Binding Name}" FontWeight="Bold" Margin="0,2" />
                                <ItemsControl ItemsSource="{Binding Items}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Border Background="#FFF" BorderBrush="#DDD" BorderThickness="1"
                                                    CornerRadius="3" Padding="6,3" Margin="4,2"
                                                    MouseMove="PaletteItem_MouseMove"
                                                    MouseLeftButtonDown="PaletteItem_MouseLeftButtonDown">
                                                <TextBlock Text="{Binding DisplayName}" />
                                            </Border>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </DockPanel>

            <!-- Canvas -->
            <Border Grid.Column="1" Background="#FAFAFA" BorderBrush="#CCC" BorderThickness="1,0,1,0">
                <ItemsControl x:Name="NodeHost" ItemsSource="{Binding Nodes}"
                              AllowDrop="True" Drop="Canvas_Drop" Background="Transparent">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <Canvas Background="Transparent" />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemContainerStyle>
                        <Style TargetType="ContentPresenter">
                            <Setter Property="Canvas.Left" Value="{Binding X}" />
                            <Setter Property="Canvas.Top" Value="{Binding Y}" />
                        </Style>
                    </ItemsControl.ItemContainerStyle>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Width="160" CornerRadius="6" Background="White"
                                    BorderBrush="{Binding IsSelected, Converter={x:Static local:SelectionBorderConverter.Instance}}"
                                    BorderThickness="2"
                                    MouseLeftButtonDown="Node_MouseLeftButtonDown">
                                <StackPanel>
                                    <Border Background="{Binding CategoryColor, Converter={StaticResource CategoryBrush}}"
                                            CornerRadius="6,6,0,0" Padding="6,3">
                                        <TextBlock Text="{Binding Label}" Foreground="White" FontWeight="Bold" />
                                    </Border>
                                    <Grid Margin="0,4">
                                        <ItemsControl ItemsSource="{Binding InputPorts}" HorizontalAlignment="Left">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <Ellipse Width="10" Height="10" Fill="#555" Margin="2" ToolTip="{Binding Name}" />
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                        <ItemsControl ItemsSource="{Binding OutputPorts}" HorizontalAlignment="Right">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <Ellipse Width="10" Height="10" Fill="#555" Margin="2" ToolTip="{Binding Name}" />
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </Grid>
                                    <TextBlock Text="{Binding TargetBadge}" FontSize="10" Foreground="#888"
                                               Margin="6,0,6,4"
                                               Visibility="{Binding TargetBadge, Converter={x:Static local:NullToCollapsedConverter.Instance}}" />
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Border>

            <!-- Properties placeholder (M4) -->
            <Border Grid.Column="2" Background="#F7F7F7">
                <TextBlock Text="Properties (M4)" Foreground="#999" Margin="8" />
            </Border>
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 5: Implement the two small XAML converters used above**

Create `BotBuilder/ValueConverters.cs`:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BotBuilder;

/// <summary>True -> blue selection border; false -> light grey.</summary>
public sealed class SelectionBorderConverter : IValueConverter
{
    public static readonly SelectionBorderConverter Instance = new();

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(value is true ? Color.FromRgb(0x29, 0x6F, 0xD6) : Color.FromRgb(0xDD, 0xDD, 0xDD));

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Null/empty -> Collapsed; otherwise Visible.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public static readonly NullToCollapsedConverter Instance = new();

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 6: Implement `MainWindow.xaml.cs` (DataContext wiring only; gestures are stubs filled in Task 6)**

Overwrite `BotBuilder/MainWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;

namespace BotBuilder;

public partial class MainWindow : Window
{
    private readonly BotEditorViewModel _editor;

    public MainWindow()
    {
        InitializeComponent();

        var registry = new ActionRegistry();
        BuiltInActions.Register(registry, new ActionExecutorRegistry());
        _editor = new BotEditorViewModel(registry);
        DataContext = _editor;
    }

    // Gesture handlers are wired in Task 6. Stubs keep the XAML compiling.
    private void New_Click(object sender, RoutedEventArgs e) { }
    private void Open_Click(object sender, RoutedEventArgs e) { }
    private void Save_Click(object sender, RoutedEventArgs e) { }
    private void PaletteItem_MouseMove(object sender, MouseEventArgs e) { }
    private void PaletteItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }
    private void Canvas_Drop(object sender, DragEventArgs e) { }
    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }
}
```

- [ ] **Step 7: Build and test**

Run: `dotnet build ADB.slnx`
Expected: build succeeds, **0 warnings, 0 errors**.
Run: `dotnet test`
Expected: 73 tests pass (no new tests this task; no regressions).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(builder): WPF shell with palette, node-card canvas, and layout"
```

**Manual Verification Checklist (user runs `dotnet run --project BotBuilder`):**
- Window opens with menu, target-bar placeholder, palette (left), empty canvas (centre), properties placeholder (right), status bar.
- Palette lists "Control Flow" (Start, End) and "Data" (Log); typing in the search box filters them live.
- (Adding/moving nodes is wired in Task 6.)

---

### Task 6: `BotBuilder` interactions — add, move, select, File menu

> **Verification:** clean `dotnet build` (0 warnings) + `dotnet test` green + Manual Verification Checklist.

**Files:**
- Modify: `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Implement the gesture + menu handlers**

Replace the stub handlers in `BotBuilder/MainWindow.xaml.cs` so the file reads exactly:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.Palette;
using Microsoft.Win32;

namespace BotBuilder;

public partial class MainWindow : Window
{
    private const string BotFilter = "Bot files (*.bot)|*.bot|All files (*.*)|*.*";

    private readonly BotEditorViewModel _editor;

    private NodeViewModel? _draggingNode;
    private Point _dragStartPointerOnCanvas;
    private double _dragStartNodeX;
    private double _dragStartNodeY;
    private Point _paletteMouseDownPoint;

    public MainWindow()
    {
        InitializeComponent();

        var registry = new ActionRegistry();
        BuiltInActions.Register(registry, new ActionExecutorRegistry());
        _editor = new BotEditorViewModel(registry);
        DataContext = _editor;
    }

    private void New_Click(object sender, RoutedEventArgs e) => _editor.New();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = BotFilter };
        if (dialog.ShowDialog(this) == true)
        {
            _editor.Open(dialog.FileName);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = BotFilter, DefaultExt = ".bot", FileName = _editor.BotName };
        if (dialog.ShowDialog(this) == true)
        {
            _editor.Save(dialog.FileName);
        }
    }

    // ---- Palette: double-click adds at centre; drag starts a drag-drop carrying the TypeKey ----

    private void PaletteItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _paletteMouseDownPoint = e.GetPosition(this);

        if (e.ClickCount == 2 && PaletteItemFrom(sender) is { } item)
        {
            var centre = new Point(NodeHost.ActualWidth / 2, NodeHost.ActualHeight / 2);
            _editor.AddNode(item.TypeKey, centre.X, centre.Y);
        }
    }

    private void PaletteItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - _paletteMouseDownPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (PaletteItemFrom(sender) is { } item)
        {
            DragDrop.DoDragDrop((DependencyObject)sender, item.TypeKey, DragDropEffects.Copy);
        }
    }

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(string)) is string typeKey)
        {
            var p = e.GetPosition(NodeHost);
            _editor.AddNode(typeKey, p.X, p.Y);
        }
    }

    // ---- Node: click selects; drag moves ----

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NodeViewModel node })
        {
            _editor.Select(node);

            _draggingNode = node;
            _dragStartPointerOnCanvas = e.GetPosition(NodeHost);
            _dragStartNodeX = node.X;
            _dragStartNodeY = node.Y;

            ((UIElement)sender).CaptureMouse();
            NodeHost.MouseMove += NodeHost_MouseMove;
            NodeHost.MouseLeftButtonUp += NodeHost_MouseLeftButtonUp;
            e.Handled = true;
        }
    }

    private void NodeHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingNode is null)
        {
            return;
        }

        var current = e.GetPosition(NodeHost);
        var dx = current.X - _dragStartPointerOnCanvas.X;
        var dy = current.Y - _dragStartPointerOnCanvas.Y;
        _editor.MoveNode(_draggingNode, _dragStartNodeX + dx, _dragStartNodeY + dy);
    }

    private void NodeHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingNode is not null)
        {
            Mouse.Capture(null);
            NodeHost.MouseMove -= NodeHost_MouseMove;
            NodeHost.MouseLeftButtonUp -= NodeHost_MouseLeftButtonUp;
            _draggingNode = null;
        }
    }

    private static PaletteItem? PaletteItemFrom(object sender)
        => (sender as FrameworkElement)?.DataContext as PaletteItem;
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build ADB.slnx`
Expected: build succeeds, **0 warnings, 0 errors**.
Run: `dotnet test`
Expected: 73 tests pass (no regressions).

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(builder): wire add/move/select gestures and File New/Open/Save"
```

**Manual Verification Checklist (user runs `dotnet run --project BotBuilder`):**
- Double-clicking a palette item adds a node card at the canvas centre.
- Dragging a palette item onto the canvas adds a node card at the drop point.
- Dragging a node card moves it; clicking it shows the blue selection border (one selected at a time).
- File ▸ Save writes a `.bot`; File ▸ New clears; File ▸ Open reloads it and the nodes reappear at their saved positions.
- Status bar shows the bot name and node count.

---

## Self-Review

**Spec coverage (M3a slice in `Docs/Specs/2026-06-01-m3-builder-canvas-design.md` §5):**
- Projects (`BotBuilder.Core`, `BotBuilder`, `BotBuilder.Core.Tests`) — Tasks 1 & 5. ✓
- Shell + §5.1 layout (menu, target-bar placeholder, palette, canvas, properties placeholder, status bar) — Task 5. ✓
- Palette grouped + search — Task 3 (VM) + Task 5 (view). ✓
- Node cards (ports, category colour, target badge) — Task 2 (VM) + Task 5 (template). ✓
- Add (double-click + drag-drop) — Task 6. ✓
- Move (drag) — Task 6. ✓
- Click-select — Task 6. ✓
- New/Open/Save `.bot` — Task 4 (editor + mapper) + Task 6 (menu/dialogs). ✓
- Tested core: editor/node/port/palette VMs, Add/Move, selection, DocumentMapper round-trip — Tasks 1–4. ✓

**Placeholder scan:** The only "placeholder" is the intentional Properties Panel stub (M4) and the Target Bar text stub (M3c) — both deliberate per the spec. No TBD/incomplete code steps; Task 5's handler stubs are explicitly completed in Task 6.

**Type consistency:** `BotEditorViewModel(ActionRegistry)`, `AddNode(typeKey,x,y)`, `MoveNode(node,x,y)`, `Select(node)`, `New/Open/Save`, `BotId/BotName/IsDirty/SelectedNode/Nodes/Palette`, `LoadFrom(...)`; `NodeViewModel.FromDefinition(def,id,label,x,y)` + full constructor; `PaletteViewModel(registry)`/`SearchText`/`Categories`; `DocumentMapper.ToBot/Populate`; `CategoryColors.ColorFor/Default` — names are used consistently across tasks and the XAML bindings (`X`,`Y`,`Label`,`IsSelected`,`CategoryColor`,`InputPorts`,`OutputPorts`,`TargetBadge`,`Palette.SearchText`,`Palette.Categories`,`BotName`,`Nodes`). ✓

**Scope:** No connections, delete, undo/redo (M3b); no pan/zoom, marquee, real Target Bar (M3c); no Properties config form (M4); no new action types (M5). ✓
