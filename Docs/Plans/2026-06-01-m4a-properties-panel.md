# M4a — Properties Panel (core fields + Target dropdown + Retry) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a node is selected, show a Properties Panel that edits its label, assigns its **Target** (completing the M3c-2 assignment story), renders its action's config fields as a form (String / MultilineString / Number / Boolean / Enum), and (for retry-capable actions) a Retry section — all persisted in the `.bot`.

**Architecture:** Tested, WPF-free logic in `BotBuilder.Core`: `NodeViewModel` gains `Config` + retry storage; a `Properties/ConfigFieldViewModel` reads/writes a node's config value with per-type normalization (handles `JsonElement` from loaded bots) and coercion; a `Properties/PropertiesViewModel` rebuilds from the editor's `SelectedNode` (resolving its `IActionDefinition` for ConfigFields/SupportsRetry) and exposes the target selection. The WPF shell replaces the "Properties (M4)" placeholder with a data-bound panel using a per-field-type `DataTemplate` selector. Per `Docs/Design/V1.md` §5.4. File/Image path fields are M4b; the image-preview/sidecar belongs to M6.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm, xUnit. Builds on merged M3.

---

## Verification model
- **Tasks 1–3 (`BotBuilder.Core`)**: strict TDD via `dotnet test`.
- **Task 4 (`BotBuilder` WPF)**: `dotnet build ADB.slnx` 0 warnings + `dotnet test` green; Manual Verification Checklist.

## File Structure
```
BotBuilder.Core/
  NodeViewModel.cs            # MODIFIED: add Config dict + RetryMaxAttempts/RetryDelayMs
  BotEditorViewModel.cs       # MODIFIED: Properties property, MarkDirty()
  DocumentMapper.cs           # MODIFIED: round-trip Config + Retry
  Properties/
    ConfigFieldViewModel.cs   # NEW: one config field's value (normalize/coerce per type)
    PropertiesViewModel.cs    # NEW: panel state for the selected node
BotBuilder/
  ConfigFieldTemplateSelector.cs  # NEW: picks a field editor template by ConfigFieldType
  MainWindow.xaml             # MODIFIED: Properties panel replaces the placeholder
  MainWindow.xaml.cs          # (no new handlers expected)
BotBuilder.Core.Tests/
  ConfigFieldViewModelTests.cs, PropertiesViewModelTests.cs, NodeConfigRetryRoundTripTests.cs
  FakeConfigurableDefinition.cs   # test double action def with varied ConfigFields + SupportsRetry
```

---

### Task 1: Node Config/Retry storage + `.bot` round-trip

**Files:**
- Modify: `BotBuilder.Core/NodeViewModel.cs`, `BotBuilder.Core/DocumentMapper.cs`
- Test: `BotBuilder.Core.Tests/NodeConfigRetryRoundTripTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/NodeConfigRetryRoundTripTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeConfigRetryRoundTripTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void NewNode_HasEmptyConfig_AndDefaultRetry()
    {
        var node = NewEditor().AddNode("data.log", 0, 0);

        Assert.NotNull(node.Config);
        Assert.Empty(node.Config);
        Assert.Equal(1, node.RetryMaxAttempts);
        Assert.Equal(0, node.RetryDelayMs);
    }

    [Fact]
    public void SaveOpen_RoundTripsConfigValues()
    {
        var e = NewEditor();
        var node = e.AddNode("data.log", 5, 5);
        node.Config["message"] = "hello world";
        var path = Path.Combine(Path.GetTempPath(), $"adb-m4a-{Guid.NewGuid():N}.bot");

        try
        {
            e.Save(path);
            var reopened = NewEditor();
            reopened.Open(path);

            var loaded = reopened.Nodes.Single(n => n.TypeKey == "data.log");
            // value comes back (as a JsonElement under the hood); compare as string
            Assert.Equal("hello world", loaded.Config["message"]!.ToString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveOpen_RoundTripsRetry_WhenConfigured()
    {
        var e = NewEditor();
        var node = e.AddNode("data.log", 0, 0);
        node.RetryMaxAttempts = 5;
        node.RetryDelayMs = 500;
        var path = Path.Combine(Path.GetTempPath(), $"adb-m4a-{Guid.NewGuid():N}.bot");

        try
        {
            e.Save(path);
            var reopened = NewEditor();
            reopened.Open(path);

            var loaded = reopened.Nodes.Single(n => n.TypeKey == "data.log");
            Assert.Equal(5, loaded.RetryMaxAttempts);
            Assert.Equal(500, loaded.RetryDelayMs);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DefaultRetry_IsNotWrittenToBot()
    {
        var e = NewEditor();
        var node = e.AddNode("data.log", 0, 0); // RetryMaxAttempts == 1 (no retry)

        var bot = DocumentMapper.ToBot(e);

        Assert.Null(bot.Actions.Single(a => a.Id == node.Id).Retry);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `NodeViewModel.Config`/`RetryMaxAttempts`/`RetryDelayMs` don't exist.

- [ ] **Step 3: Add storage to `NodeViewModel`**

In `BotBuilder.Core/NodeViewModel.cs`, add (alongside the other `[ObservableProperty]` fields, e.g. after `_targetId`):
```csharp
    [ObservableProperty] private int _retryMaxAttempts = 1;
    [ObservableProperty] private int _retryDelayMs;
```
And add a config dictionary property (next to the read-only `Id`/`TypeKey` members):
```csharp
    /// <summary>Action-specific settings, keyed by config-field key.</summary>
    public Dictionary<string, object> Config { get; } = new();
```

- [ ] **Step 4: Round-trip in `DocumentMapper`**

In `BotBuilder.Core/DocumentMapper.cs`:

(a) In `ToBot`, in the node→action loop, write Config + Retry. Replace the `bot.Actions.Add(new BotAction { ... });` for nodes with:
```csharp
        foreach (var node in editor.Nodes)
        {
            var action = new BotAction
            {
                Id = node.Id,
                TypeKey = node.TypeKey,
                Label = node.Label,
                TargetId = node.TargetId,
                CanvasPosition = new Position { X = node.X, Y = node.Y },
                Config = new Dictionary<string, object>(node.Config),
            };
            if (node.RetryMaxAttempts > 1)
            {
                action.Retry = new RetryPolicy { MaxAttempts = node.RetryMaxAttempts, DelayMs = node.RetryDelayMs };
            }
            bot.Actions.Add(action);
        }
```
(This replaces the existing nodes loop in `ToBot`. Keep the connections loop and targets loop exactly as they are.)

(b) In `BuildNode` (used by `Populate`), after the node is created and `node.TargetId = action.TargetId;` is set, also copy config + retry. Add before `return node;`:
```csharp
        foreach (var kv in action.Config) { node.Config[kv.Key] = kv.Value; }
        node.RetryMaxAttempts = action.Retry?.MaxAttempts ?? 1;
        node.RetryDelayMs = action.Retry?.DelayMs ?? 0;
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all pass (133 prior + 4 new = 137), 0 failures. Existing mapper/serializer tests still pass (Config/Retry are additive; bots without them round-trip — empty config, RetryMaxAttempts 1).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(builder): node config + retry storage with .bot round-trip"
```

---

### Task 2: `ConfigFieldViewModel`

A view-model for one config field: exposes `Value` (object) that reads the node's `Config[key]` (or the field's default) normalized to the field's CLR type for display, and on set coerces the UI input to the right CLR type for storage. Handles `JsonElement` values that come from loaded `.bot` files.

**Files:**
- Create: `BotBuilder.Core/Properties/ConfigFieldViewModel.cs`
- Test: `BotBuilder.Core.Tests/ConfigFieldViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/ConfigFieldViewModelTests.cs`:
```csharp
using System.Text.Json;
using AdbCore.Actions;
using BotBuilder.Core;
using BotBuilder.Core.Properties;
using Xunit;

namespace BotBuilder.Core.Tests;

public class ConfigFieldViewModelTests
{
    private static NodeViewModel Node()
        => new(Guid.NewGuid(), "x", "X", "Test", Array.Empty<PortViewModel>(), Array.Empty<PortViewModel>(), 0, 0);

    private static ConfigFieldViewModel Field(NodeViewModel node, ConfigFieldType type, object? @default = null, params string[] options)
        => new(node, new ConfigField { Key = "k", Label = "K", Type = type, DefaultValue = @default, Options = options.ToList() }, () => { });

    [Fact]
    public void String_AbsentKey_ReturnsDefault()
    {
        var f = Field(Node(), ConfigFieldType.String, @default: "fallback");

        Assert.Equal("fallback", f.Value);
    }

    [Fact]
    public void String_Set_StoresStringInConfig()
    {
        var node = Node();
        var f = Field(node, ConfigFieldType.String);

        f.Value = "hi";

        Assert.Equal("hi", node.Config["k"]);
    }

    [Fact]
    public void Number_SetFromString_StoresDouble()
    {
        var node = Node();
        var f = Field(node, ConfigFieldType.Number);

        f.Value = "0.9"; // as a TextBox would supply it

        Assert.Equal(0.9, Assert.IsType<double>(node.Config["k"]));
    }

    [Fact]
    public void Number_FromJsonElement_ReadsAsDouble()
    {
        var node = Node();
        node.Config["k"] = JsonDocument.Parse("0.75").RootElement; // as a loaded .bot supplies it
        var f = Field(node, ConfigFieldType.Number);

        Assert.Equal(0.75, Assert.IsType<double>(f.Value));
    }

    [Fact]
    public void Boolean_SetTrue_StoresBool_AndReadsBackFromJsonElement()
    {
        var node = Node();
        var f = Field(node, ConfigFieldType.Boolean);

        f.Value = true;
        Assert.True(Assert.IsType<bool>(node.Config["k"]));

        node.Config["k"] = JsonDocument.Parse("true").RootElement;
        Assert.True(Assert.IsType<bool>(f.Value));
    }

    [Fact]
    public void Set_InvokesOnChanged()
    {
        var node = Node();
        var changed = 0;
        var f = new ConfigFieldViewModel(node, new ConfigField { Key = "k", Type = ConfigFieldType.String }, () => changed++);

        f.Value = "z";

        Assert.Equal(1, changed);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `ConfigFieldViewModel` does not exist.

- [ ] **Step 3: Implement `ConfigFieldViewModel`**

Create `BotBuilder.Core/Properties/ConfigFieldViewModel.cs`:
```csharp
using System.Text.Json;
using AdbCore.Actions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Properties;

/// <summary>Editable value for one action config field. Normalizes the stored value (possibly a
/// <see cref="JsonElement"/> from a loaded bot) to the field's CLR type for display, and coerces
/// UI input back to that type on write.</summary>
public partial class ConfigFieldViewModel : ObservableObject
{
    private readonly NodeViewModel _node;
    private readonly Action _onChanged;

    public ConfigFieldViewModel(NodeViewModel node, ConfigField field, Action onChanged)
    {
        _node = node;
        Field = field;
        _onChanged = onChanged;
    }

    public ConfigField Field { get; }
    public string Key => Field.Key;
    public string Label => Field.Label;
    public ConfigFieldType Type => Field.Type;
    public IReadOnlyList<string> Options => Field.Options;

    public object? Value
    {
        get => Normalize(_node.Config.TryGetValue(Field.Key, out var v) ? v : Field.DefaultValue);
        set
        {
            _node.Config[Field.Key] = Coerce(value);
            OnPropertyChanged();
            _onChanged();
        }
    }

    private object? Normalize(object? raw)
    {
        if (raw is JsonElement json)
        {
            return Type switch
            {
                ConfigFieldType.Number => json.ValueKind == JsonValueKind.Number ? json.GetDouble() : 0d,
                ConfigFieldType.Boolean => json.ValueKind is JsonValueKind.True or JsonValueKind.False && json.GetBoolean(),
                _ => json.ValueKind == JsonValueKind.String ? json.GetString() ?? string.Empty : json.ToString(),
            };
        }

        return Type switch
        {
            ConfigFieldType.Number => raw is double d ? d : double.TryParse(raw?.ToString(), out var n) ? n : 0d,
            ConfigFieldType.Boolean => raw is bool b ? b : bool.TryParse(raw?.ToString(), out var bb) && bb,
            _ => raw?.ToString() ?? string.Empty,
        };
    }

    private object Coerce(object? input)
    {
        return Type switch
        {
            ConfigFieldType.Number => input is double d ? d : double.TryParse(input?.ToString(), out var n) ? n : 0d,
            ConfigFieldType.Boolean => input is bool b ? b : bool.TryParse(input?.ToString(), out var bb) && bb,
            _ => input?.ToString() ?? string.Empty,
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all pass (137 prior + 6 new = 143), 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(builder): add config-field value view-model with per-type normalization"
```

---

### Task 3: `PropertiesViewModel` + editor wiring

**Files:**
- Modify: `BotBuilder.Core/BotEditorViewModel.cs` (add `Properties` + `MarkDirty()`)
- Create: `BotBuilder.Core/Properties/PropertiesViewModel.cs`
- Test: `BotBuilder.Core.Tests/PropertiesViewModelTests.cs`, `BotBuilder.Core.Tests/FakeConfigurableDefinition.cs`

- [ ] **Step 1: Write the failing tests + a configurable fake definition**

Create `BotBuilder.Core.Tests/FakeConfigurableDefinition.cs`:
```csharp
using AdbCore.Actions;

namespace BotBuilder.Core.Tests;

/// <summary>A test action definition with varied config fields and retry support.</summary>
internal sealed class FakeConfigurableDefinition : IActionDefinition
{
    public string TypeKey => "test.configurable";
    public string DisplayName => "Configurable";
    public string Category => "Test";
    public string Description => "A configurable test action.";
    public List<PortDefinition> InputPorts { get; } = new();
    public List<PortDefinition> OutputPorts { get; } = new();
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "msg", Label = "Message", Type = ConfigFieldType.String },
        new ConfigField { Key = "count", Label = "Count", Type = ConfigFieldType.Number },
        new ConfigField { Key = "flag", Label = "Flag", Type = ConfigFieldType.Boolean },
    };
    public bool SupportsRetry => true;
}
```

Create `BotBuilder.Core.Tests/PropertiesViewModelTests.cs`:
```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class PropertiesViewModelTests
{
    private static BotEditorViewModel BuiltInEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static BotEditorViewModel ConfigurableEditor()
    {
        var defs = new ActionRegistry();
        defs.Register(new FakeConfigurableDefinition());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void NoSelection_HasNoNode_NoFields()
    {
        var e = BuiltInEditor();

        Assert.Null(e.Properties.Node);
        Assert.Empty(e.Properties.Fields);
        Assert.False(e.Properties.SupportsRetry);
    }

    [Fact]
    public void SelectingLogNode_ExposesItsMessageField()
    {
        var e = BuiltInEditor();
        var node = e.AddNode("data.log", 0, 0);

        e.Select(node);

        Assert.Same(node, e.Properties.Node);
        Assert.Equal("Log", e.Properties.ActionTitle);
        var field = Assert.Single(e.Properties.Fields);
        Assert.Equal("message", field.Key);
        Assert.False(e.Properties.SupportsRetry);
    }

    [Fact]
    public void Deselecting_ClearsFields()
    {
        var e = BuiltInEditor();
        e.Select(e.AddNode("data.log", 0, 0));

        e.Select(null);

        Assert.Null(e.Properties.Node);
        Assert.Empty(e.Properties.Fields);
    }

    [Fact]
    public void ConfigurableAction_ExposesAllFields_AndSupportsRetry()
    {
        var e = ConfigurableEditor();
        var node = e.AddNode("test.configurable", 0, 0);

        e.Select(node);

        Assert.Equal(3, e.Properties.Fields.Count);
        Assert.True(e.Properties.SupportsRetry);
    }

    [Fact]
    public void SelectedTargetId_Setter_AssignsTarget()
    {
        var e = BuiltInEditor();
        var node = e.AddNode("data.log", 0, 0);
        e.TargetBar.AddTarget();
        var t2 = e.TargetBar.AddTarget();
        e.Select(node);

        e.Properties.SelectedTargetId = t2.Id;

        Assert.Equal(t2.Id, node.TargetId);
    }

    [Fact]
    public void EditingAField_MarksEditorDirty()
    {
        var e = BuiltInEditor();
        var node = e.AddNode("data.log", 0, 0);
        e.Select(node);
        e.Save(Path.Combine(Path.GetTempPath(), $"adb-{Guid.NewGuid():N}.bot")); // clears IsDirty
        var savedPath = e.FilePath!;

        try
        {
            e.Properties.Fields[0].Value = "edited";
            Assert.True(e.IsDirty);
        }
        finally
        {
            if (File.Exists(savedPath)) File.Delete(savedPath);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests`
Expected: build FAILS — `BotEditorViewModel.Properties`/`MarkDirty` and `PropertiesViewModel` don't exist.

- [ ] **Step 3: Add `MarkDirty` + `Properties` to the editor**

In `BotBuilder.Core/BotEditorViewModel.cs`:

(a) Add the using:
```csharp
using BotBuilder.Core.Properties;
```

(b) In the constructor, after `TargetBar` is created and wired (and before `New();`), create the properties view-model:
```csharp
        Properties = new PropertiesViewModel(this, registry);
```
(Note: the ctor parameter is named `registry`. Place this line after the `TargetBar.Changed += ...` line.)

(c) Add the property (near `Palette`/`Viewport`/`TargetBar`):
```csharp
    public PropertiesViewModel Properties { get; }
```

(d) Add a public dirty marker (used by config-field edits, which are not undoable in M4a):
```csharp
    /// <summary>Marks the document dirty (used by property edits that don't go through the undo stack).</summary>
    public void MarkDirty() => IsDirty = true;
```

- [ ] **Step 4: Implement `PropertiesViewModel`**

Create `BotBuilder.Core/Properties/PropertiesViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using AdbCore.Actions;
using BotBuilder.Core.Targets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Properties;

/// <summary>State for the Properties Panel: the selected node, its action's config fields, target
/// selection, and retry visibility. Rebuilds whenever the editor's selected node changes.</summary>
public partial class PropertiesViewModel : ObservableObject
{
    private readonly BotEditorViewModel _editor;
    private readonly ActionRegistry _registry;

    [ObservableProperty] private NodeViewModel? _node;
    [ObservableProperty] private bool _supportsRetry;
    [ObservableProperty] private string _actionTitle = string.Empty;

    public PropertiesViewModel(BotEditorViewModel editor, ActionRegistry registry)
    {
        _editor = editor;
        _registry = registry;
        Fields = new ObservableCollection<ConfigFieldViewModel>();
        editor.PropertyChanged += OnEditorPropertyChanged;
        Rebuild();
    }

    public ObservableCollection<ConfigFieldViewModel> Fields { get; }

    /// <summary>The configured targets, for the target dropdown.</summary>
    public IReadOnlyList<TargetViewModel> Targets => _editor.TargetBar.Targets;

    /// <summary>The selected node's assigned target id (null = the default first target).</summary>
    public Guid? SelectedTargetId
    {
        get => Node?.TargetId;
        set
        {
            if (Node is not null)
            {
                _editor.AssignTarget(Node, value);
            }
        }
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BotEditorViewModel.SelectedNode))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        Node = _editor.SelectedNode;
        Fields.Clear();

        if (Node is null)
        {
            SupportsRetry = false;
            ActionTitle = string.Empty;
        }
        else
        {
            var definition = _registry.TryGet(Node.TypeKey, out var def) ? def : null;
            SupportsRetry = definition?.SupportsRetry ?? false;
            ActionTitle = definition?.DisplayName ?? Node.TypeKey;

            if (definition is not null)
            {
                foreach (var field in definition.ConfigFields)
                {
                    Fields.Add(new ConfigFieldViewModel(Node, field, _editor.MarkDirty));
                }
            }
        }

        OnPropertyChanged(nameof(SelectedTargetId));
        OnPropertyChanged(nameof(Targets));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all pass (143 prior + 6 new = 149), 0 failures. Existing tests still green (`Properties` is additive; `Select(null)` already existed via `Select`).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(builder): add properties view-model (selected node fields, target, retry)"
```

---

### Task 4: WPF — Properties Panel

> **Verification:** clean `dotnet build` (0 warnings) + `dotnet test` (149) + Manual Verification Checklist.

**Files:**
- Create: `BotBuilder/ConfigFieldTemplateSelector.cs`
- Modify: `BotBuilder/MainWindow.xaml`

- [ ] **Step 1: Add the field-template selector**

Create `BotBuilder/ConfigFieldTemplateSelector.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using BotBuilder.Core.Actions;
using BotBuilder.Core.Properties;

namespace BotBuilder;

/// <summary>Chooses a config-field editor template based on the field's type.</summary>
public sealed class ConfigFieldTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StringTemplate { get; set; }
    public DataTemplate? MultilineTemplate { get; set; }
    public DataTemplate? NumberTemplate { get; set; }
    public DataTemplate? BooleanTemplate { get; set; }
    public DataTemplate? EnumTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not ConfigFieldViewModel field)
        {
            return base.SelectTemplate(item, container);
        }

        return field.Type switch
        {
            AdbCore.Actions.ConfigFieldType.MultilineString => MultilineTemplate,
            AdbCore.Actions.ConfigFieldType.Number => NumberTemplate,
            AdbCore.Actions.ConfigFieldType.Boolean => BooleanTemplate,
            AdbCore.Actions.ConfigFieldType.Enum => EnumTemplate,
            _ => StringTemplate, // String, FilePath, ImagePath (the path types get real editors in M4b)
        };
    }
}
```
Note: `ConfigFieldType` lives in `AdbCore.Actions` — the `using BotBuilder.Core.Actions;` line above is NOT needed; remove it if the implementer's IDE flags it. Keep `using BotBuilder.Core.Properties;` (for `ConfigFieldViewModel`) and reference the enum as `AdbCore.Actions.ConfigFieldType` as shown.

- [ ] **Step 2: Replace the Properties placeholder with the panel**

In `BotBuilder/MainWindow.xaml`:

(a) Add the BCL converter + a CLR namespace for the selector. In `<Window.Resources>`, alongside the existing converter, add the field templates, the selector, and a `BooleanToVisibilityConverter`. Replace the `<Window.Resources>` block:
```xml
    <Window.Resources>
        <local:CategoryColorToBrushConverter x:Key="CategoryBrush" />
    </Window.Resources>
```
with:
```xml
    <Window.Resources>
        <local:CategoryColorToBrushConverter x:Key="CategoryBrush" />
        <BooleanToVisibilityConverter x:Key="BoolToVis" />

        <DataTemplate x:Key="FieldString">
            <StackPanel Margin="0,4">
                <TextBlock Text="{Binding Label}" FontSize="11" Foreground="#666" />
                <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
        </DataTemplate>
        <DataTemplate x:Key="FieldMultiline">
            <StackPanel Margin="0,4">
                <TextBlock Text="{Binding Label}" FontSize="11" Foreground="#666" />
                <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}"
                         AcceptsReturn="True" TextWrapping="Wrap" MinHeight="48" VerticalScrollBarVisibility="Auto" />
            </StackPanel>
        </DataTemplate>
        <DataTemplate x:Key="FieldNumber">
            <StackPanel Margin="0,4">
                <TextBlock Text="{Binding Label}" FontSize="11" Foreground="#666" />
                <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
        </DataTemplate>
        <DataTemplate x:Key="FieldBoolean">
            <CheckBox Content="{Binding Label}" IsChecked="{Binding Value}" Margin="0,6" />
        </DataTemplate>
        <DataTemplate x:Key="FieldEnum">
            <StackPanel Margin="0,4">
                <TextBlock Text="{Binding Label}" FontSize="11" Foreground="#666" />
                <ComboBox ItemsSource="{Binding Options}" SelectedItem="{Binding Value}" />
            </StackPanel>
        </DataTemplate>

        <local:ConfigFieldTemplateSelector x:Key="FieldSelector"
            StringTemplate="{StaticResource FieldString}"
            MultilineTemplate="{StaticResource FieldMultiline}"
            NumberTemplate="{StaticResource FieldNumber}"
            BooleanTemplate="{StaticResource FieldBoolean}"
            EnumTemplate="{StaticResource FieldEnum}" />
    </Window.Resources>
```

(b) Replace the properties placeholder column:
```xml
            <Border Grid.Column="2" Background="#F7F7F7">
                <TextBlock Text="Properties (M4)" Foreground="#999" Margin="8" />
            </Border>
```
with the data-bound panel:
```xml
            <Border Grid.Column="2" Background="#F7F7F7" BorderBrush="#CCC" BorderThickness="1,0,0,0">
                <Grid DataContext="{Binding Properties}">
                    <TextBlock Text="Select a node to edit its properties." Foreground="#999" Margin="8"
                               TextWrapping="Wrap">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Setter Property="Visibility" Value="Collapsed" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding Node}" Value="{x:Null}">
                                        <Setter Property="Visibility" Value="Visible" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>

                    <ScrollViewer VerticalScrollBarVisibility="Auto">
                        <ScrollViewer.Style>
                            <Style TargetType="ScrollViewer">
                                <Setter Property="Visibility" Value="Visible" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding Node}" Value="{x:Null}">
                                        <Setter Property="Visibility" Value="Collapsed" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </ScrollViewer.Style>
                        <StackPanel Margin="8">
                            <TextBlock Text="{Binding ActionTitle}" FontWeight="Bold" FontSize="14" Margin="0,0,0,6" />

                            <TextBlock Text="Label" FontSize="11" Foreground="#666" />
                            <TextBox Text="{Binding Node.Label, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,8" />

                            <TextBlock Text="Target" FontSize="11" Foreground="#666" />
                            <ComboBox ItemsSource="{Binding Targets}" DisplayMemberPath="Name"
                                      SelectedValuePath="Id" SelectedValue="{Binding SelectedTargetId}"
                                      Margin="0,0,0,8" ToolTip="Unset = the first target" />

                            <ItemsControl ItemsSource="{Binding Fields}"
                                          ItemTemplateSelector="{StaticResource FieldSelector}" />

                            <Border BorderBrush="#DDD" BorderThickness="0,1,0,0" Margin="0,10,0,0" Padding="0,8,0,0"
                                    Visibility="{Binding SupportsRetry, Converter={StaticResource BoolToVis}}">
                                <StackPanel>
                                    <TextBlock Text="Retry" FontWeight="Bold" Margin="0,0,0,4" />
                                    <TextBlock Text="Max Attempts" FontSize="11" Foreground="#666" />
                                    <TextBox Text="{Binding Node.RetryMaxAttempts, UpdateSourceTrigger=PropertyChanged}" />
                                    <TextBlock Text="Delay (ms)" FontSize="11" Foreground="#666" Margin="0,4,0,0" />
                                    <TextBox Text="{Binding Node.RetryDelayMs, UpdateSourceTrigger=PropertyChanged}" />
                                </StackPanel>
                            </Border>
                        </StackPanel>
                    </ScrollViewer>
                </Grid>
            </Border>
```

- [ ] **Step 3: Build and test**

Run: `dotnet build ADB.slnx` → expect 0 warnings, 0 errors. (MSB3021/MSB3027 exe-lock → app running; report. Compile must be clean. If the `DataContext="{Binding Properties}"` panel or the selector reference needs a tweak to compile, fix cleanly and note it — `Properties` is a property on the editor (the window DataContext), `local:ConfigFieldTemplateSelector` resolves via the existing `xmlns:local`.)
Run: `dotnet test` → 149 pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(builder): Properties Panel (label, target dropdown, config fields, retry)"
```

**Manual Verification Checklist (`dotnet run --project BotBuilder`):**
- With nothing selected, the right panel says "Select a node to edit its properties."
- Select a Log node → the panel shows the action title ("Log"), an editable **Label**, a **Target** dropdown, and a **Message** field (String). Typing in Message updates the node's config (verify by Save → reopen: the message persists).
- Add 2 targets, select a node, pick a target in the dropdown → the node's badge updates to that target's name.
- Editing the Label updates the node card's header live.
- (Retry section only appears for retry-capable actions; none exist until M5, so it stays hidden for Start/End/Log — expected.)
- Selecting a different node swaps the panel to that node's fields; clicking empty canvas (deselect) returns to the "Select a node" message.

---

## Self-Review

**Spec coverage (design §5.4, M4a portion):**
- Config fields rendered as a form (String/Multiline/Number/Boolean/Enum) — Task 2 (`ConfigFieldViewModel`) + Task 4 (templates + selector). ✓
- Target dropdown at top, lists targets by name, sets assignment — Task 3 (`SelectedTargetId`) + Task 4 (ComboBox). ✓
- Retry section for `SupportsRetry` actions (Max Attempts + Delay) — Task 1 (storage) + Task 3 (`SupportsRetry`) + Task 4 (conditional section). ✓
- Config + Retry persisted — Task 1 (`DocumentMapper`). ✓
- (FilePath/ImagePath field types + image preview are M4b/M6 — String template is the interim fallback for path types.)

**Placeholder scan:** No TBD/placeholder steps; the only removed placeholder is the "Properties (M4)" text, replaced by the real panel.

**Type consistency:** `NodeViewModel.Config`/`RetryMaxAttempts`/`RetryDelayMs`; `ConfigFieldViewModel(node, field, onChanged)` + `Value`/`Label`/`Type`/`Options`; `PropertiesViewModel` (`Node`/`Fields`/`Targets`/`SelectedTargetId`/`SupportsRetry`/`ActionTitle`); editor `Properties`/`MarkDirty`; `DocumentMapper` Config+Retry round-trip — names match across tasks and XAML bindings (`Properties`, `Node.Label`, `Targets`, `SelectedTargetId`, `Fields`, `Value`, `Options`, `SupportsRetry`, `Node.RetryMaxAttempts`/`RetryDelayMs`, `ActionTitle`). `ConfigFieldType` is referenced from `AdbCore.Actions`. ✓

**Scope:** No FilePath/ImagePath editors (M4b), no image preview/sidecar (M6), config edits not on the undo stack (consistent with target ops). ✓
