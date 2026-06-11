# Nested Bots — Card + Properties UI Slice (B3b) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Nested Bot cards usable via the import-driven workflow: the card renders distinctly with the referenced bot's name on it, and the properties panel lets you pick an existing library entry, import a `.bot` file as a new entry, rename it (live), remove it, and toggle the three sharing flags. No child editor in this slice.

**Architecture:** Testable VM logic in BotBuilder.Core (`NodeViewModel.Subtitle` + a distinct accent, a `NestedBotCardInfo` subtitle resolver, editor refresh, and `PropertiesViewModel` nested-bot members that drive the panel through the merged `NestedBotLibrary`). Thin WPF on top: a distinct card visual (accent + boxed border + subtitle + glyph) and a properties-panel section (themed ComboBox + Import/Rename/Remove). The three boolean flags already render as checkboxes via the existing generic `ConfigField` Boolean template — no extra work for those.

**Tech Stack:** .NET 10 WPF, BotBuilder.Core (testable VMs), CommunityToolkit.Mvvm, xUnit.

Reference spec: `Docs/superpowers/specs/2026-06-10-title-bar-and-nested-bots-design.md` (Feature B, sections B1/B2/B4). Builds on merged B1/B2/B3a (the engine + `NestedBotLibrary`). The modeless child editor (New-empty, double-click-to-edit) is the SEPARATE follow-up slice B3c — do NOT build it here.

Work in worktree `C:\git\ADB-nested-ui` (branch `worktree-nested-bots-ui`). Build/test from the worktree root. THEME RULE: every new control uses `DynamicResource` theme brushes; the ComboBox is already templated in the shared `AdbUi.Theme/Themes/Controls.xaml`, so a plain `<ComboBox>` inherits theming — do not hard-code colors.

---

### Task 1: Card subtitle + distinct accent + resolver + editor refresh

**Files:**
- Modify: `BotBuilder.Core/CategoryColors.cs`
- Modify: `BotBuilder.Core/NodeViewModel.cs`
- Create: `BotBuilder.Core/NestedBots/NestedBotCardInfo.cs`
- Modify: `BotBuilder.Core/BotEditorViewModel.cs`
- Modify: `BotBuilder.Core/DocumentMapper.cs`
- Test: `BotBuilder.Core.Tests/NestedBots/NestedBotCardInfoTests.cs` (create)
- Test: `BotBuilder.Core.Tests/NestedBots/NestedBotSubtitleTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/NestedBots/NestedBotCardInfoTests.cs`:

```csharp
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotCardInfoTests
{
    [Fact]
    public void Resolve_Unassigned_ReturnsPlaceholder()
    {
        var lib = new NestedBotLibrary();
        Assert.Equal(NestedBotCardInfo.Unassigned, NestedBotCardInfo.Resolve(new Dictionary<string, object>(), lib));
    }

    [Fact]
    public void Resolve_Assigned_ReturnsName()
    {
        var lib = new NestedBotLibrary();
        var bot = lib.AddNew("GoToPlayerMenu");
        var config = new Dictionary<string, object> { [NestedBotAction.NestedBotIdKey] = bot.Id.ToString() };
        Assert.Equal("GoToPlayerMenu", NestedBotCardInfo.Resolve(config, lib));
    }

    [Fact]
    public void Resolve_MissingReference_ReturnsMissing()
    {
        var lib = new NestedBotLibrary();
        var config = new Dictionary<string, object> { [NestedBotAction.NestedBotIdKey] = Guid.NewGuid().ToString() };
        Assert.Equal(NestedBotCardInfo.Missing, NestedBotCardInfo.Resolve(config, lib));
    }
}
```

Create `BotBuilder.Core.Tests/NestedBots/NestedBotSubtitleTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.NestedBots;
using Xunit;

namespace BotBuilder.Core.Tests.NestedBots;

public class NestedBotSubtitleTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void NestedBotNode_HasDistinctAccentColor()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        Assert.Equal(CategoryColors.NestedBot, node.CategoryColor);
        Assert.NotEqual(CategoryColors.ColorFor("Control Flow"), node.CategoryColor);
    }

    [Fact]
    public void RefreshNestedBotSubtitles_SetsNameOrPlaceholder()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        Assert.Equal(NestedBotCardInfo.Unassigned, node.Subtitle); // refreshed on add

        var bot = editor.NestedBotLibrary.AddNew("Sub");
        node.Config[NestedBotAction.NestedBotIdKey] = bot.Id.ToString();
        editor.RefreshNestedBotSubtitles();
        Assert.Equal("Sub", node.Subtitle);
    }

    [Fact]
    public void NonNestedNode_HasNullSubtitle()
    {
        var editor = NewEditor();
        var node = editor.AddNode("control.start", 0, 0);
        Assert.Null(node.Subtitle);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotCardInfoTests|FullyQualifiedName~NestedBotSubtitleTests"`
Expected: FAIL — `CategoryColors.NestedBot`, `NodeViewModel.Subtitle`, `NestedBotCardInfo`, `RefreshNestedBotSubtitles` don't exist.

- [ ] **Step 3a: Add the distinct accent**

In `BotBuilder.Core/CategoryColors.cs`, add after the `Default` const:
```csharp
    /// <summary>Distinct accent for Nested Bot cards (so they read apart from other Control Flow nodes).</summary>
    public const string NestedBot = "#00838F";
```

- [ ] **Step 3b: Add `NodeViewModel.Subtitle` + accent override**

In `BotBuilder.Core/NodeViewModel.cs`:
- Add an observable field with the others: `[ObservableProperty] private string? _subtitle;`
- Replace the `CategoryColor` property:
```csharp
    public string CategoryColor => CategoryColors.ColorFor(Category);
```
with:
```csharp
    public string CategoryColor =>
        TypeKey == NestedBotAction.NestedBotTypeKey ? CategoryColors.NestedBot : CategoryColors.ColorFor(Category);
```
(`AdbCore.Actions.BuiltIn` is already imported.)

- [ ] **Step 3c: Create the resolver**

Create `BotBuilder.Core/NestedBots/NestedBotCardInfo.cs`:
```csharp
using AdbCore.Actions.BuiltIn;

namespace BotBuilder.Core.NestedBots;

/// <summary>Resolves the secondary line shown on a Nested Bot card: the referenced library bot's name, or a
/// placeholder when unassigned or dangling.</summary>
public static class NestedBotCardInfo
{
    public const string Unassigned = "(no bot assigned)";
    public const string Missing = "(missing bot)";

    public static string Resolve(IReadOnlyDictionary<string, object> config, NestedBotLibrary library)
    {
        if (!config.TryGetValue(NestedBotAction.NestedBotIdKey, out var raw)
            || !Guid.TryParse(raw?.ToString(), out var id))
        {
            return Unassigned;
        }
        return library.Get(id)?.Name ?? Missing;
    }
}
```

- [ ] **Step 3d: Editor refresh**

In `BotBuilder.Core/BotEditorViewModel.cs`:
- Add `using BotBuilder.Core.NestedBots;` if not present.
- Add the method (near `RefreshTargetBadges`):
```csharp
    /// <summary>Sets each Nested Bot node's Subtitle to its referenced bot's name (or a placeholder).</summary>
    public void RefreshNestedBotSubtitles()
    {
        foreach (var node in Nodes)
        {
            if (node.TypeKey == AdbCore.Actions.BuiltIn.NestedBotAction.NestedBotTypeKey)
            {
                node.Subtitle = NestedBotCardInfo.Resolve(node.Config, NestedBotLibrary);
            }
        }
    }
```
- Call it at the end of `AfterEdit()` (so newly added/pasted nested cards get a subtitle):
```csharp
        RefreshNestedBotSubtitles();
```

- [ ] **Step 3e: Refresh on load**

In `BotBuilder.Core/DocumentMapper.cs` `Populate`, after `editor.NestedBotLibrary.Load(bot.NestedBots);` (added in B3a) and before/after `editor.RefreshTargetBadges();`, add:
```csharp
        editor.RefreshNestedBotSubtitles();
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotCardInfoTests|FullyQualifiedName~NestedBotSubtitleTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/CategoryColors.cs BotBuilder.Core/NodeViewModel.cs BotBuilder.Core/NestedBots/NestedBotCardInfo.cs BotBuilder.Core/BotEditorViewModel.cs BotBuilder.Core/DocumentMapper.cs BotBuilder.Core.Tests/NestedBots/NestedBotCardInfoTests.cs BotBuilder.Core.Tests/NestedBots/NestedBotSubtitleTests.cs
git commit -m "Nested Bot card: distinct accent + subtitle resolver + editor refresh"
```

---

### Task 2: `PropertiesViewModel` nested-bot members

**Files:**
- Modify: `BotBuilder.Core/Properties/PropertiesViewModel.cs`
- Test: `BotBuilder.Core.Tests/Properties/NestedBotPropertiesTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `BotBuilder.Core.Tests/Properties/NestedBotPropertiesTests.cs`:

```csharp
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests.Properties;

public class NestedBotPropertiesTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void IsNestedBotCard_TrueForNestedNode()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);
        Assert.True(editor.Properties.IsNestedBotCard);
    }

    [Fact]
    public void IsNestedBotCard_FalseForOtherNode()
    {
        var editor = NewEditor();
        var node = editor.AddNode("control.start", 0, 0);
        editor.Select(node);
        Assert.False(editor.Properties.IsNestedBotCard);
    }

    [Fact]
    public void SelectedNestedBotId_RoundTripsToConfig()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);
        var bot = editor.NestedBotLibrary.AddNew("Sub");

        editor.Properties.SelectedNestedBotId = bot.Id;

        Assert.Equal(bot.Id.ToString(), node.Config[NestedBotAction.NestedBotIdKey]);
        Assert.Equal(bot.Id, editor.Properties.SelectedNestedBotId);
        Assert.Equal("Sub", editor.Properties.SelectedNestedBotName);
        Assert.Equal("Sub", node.Subtitle); // assignment refreshed the card
    }

    [Fact]
    public void ImportNestedBot_AddsEntryAndAssignsIt()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);
        var external = new Bot { Id = Guid.NewGuid(), Name = "Imported" };

        var entry = editor.Properties.ImportNestedBot(external);

        Assert.Contains(entry, editor.NestedBotLibrary.Entries);
        Assert.Equal(entry.Id, editor.Properties.SelectedNestedBotId);
        Assert.Equal("Imported", node.Subtitle);
    }

    [Fact]
    public void EditableName_RenamesLibraryEntryLive()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);
        var bot = editor.NestedBotLibrary.AddNew("Old");
        editor.Properties.SelectedNestedBotId = bot.Id;

        editor.Properties.SelectedNestedBotEditableName = "GoToPlayerMenu";

        Assert.Equal("GoToPlayerMenu", editor.NestedBotLibrary.Get(bot.Id)!.Name);
        Assert.Equal("GoToPlayerMenu", node.Subtitle);
    }

    [Fact]
    public void RemoveSelectedNestedBot_RemovesFromLibraryAndUnassigns()
    {
        var editor = NewEditor();
        var node = editor.AddNode(NestedBotAction.NestedBotTypeKey, 0, 0);
        editor.Select(node);
        var bot = editor.NestedBotLibrary.AddNew("Sub");
        editor.Properties.SelectedNestedBotId = bot.Id;

        editor.Properties.RemoveSelectedNestedBot();

        Assert.Empty(editor.NestedBotLibrary.Entries);
        Assert.Null(editor.Properties.SelectedNestedBotId);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotPropertiesTests"`
Expected: FAIL — members don't exist.

- [ ] **Step 3: Add the members**

In `BotBuilder.Core/Properties/PropertiesViewModel.cs`:
- Add usings: `using AdbCore.Models;` and `using BotBuilder.Core.NestedBots;`.
- Add these members (after `SupportsRegionPicking`):
```csharp
    /// <summary>Whether the selected node is a Nested Bot card (drives the panel's nested-bot section).</summary>
    public bool IsNestedBotCard => Node is not null && Node.TypeKey == NestedBotAction.NestedBotTypeKey;

    /// <summary>The library entries for the picker dropdown. A fresh list each get so a rename re-renders.</summary>
    public IReadOnlyList<Bot> NestedBotEntries => _editor.NestedBotLibrary.Entries.ToList();

    /// <summary>The selected card's referenced library bot id (null = unassigned).</summary>
    public Guid? SelectedNestedBotId
    {
        get => Node is not null
            && Node.Config.TryGetValue(NestedBotAction.NestedBotIdKey, out var raw)
            && Guid.TryParse(raw?.ToString(), out var id) ? id : null;
        set
        {
            if (Node is null) { return; }
            if (value is Guid id) { Node.Config[NestedBotAction.NestedBotIdKey] = id.ToString(); }
            else { Node.Config.Remove(NestedBotAction.NestedBotIdKey); }
            _editor.MarkDirty();
            _editor.RefreshNestedBotSubtitles();
            OnPropertyChanged(nameof(SelectedNestedBotName));
            OnPropertyChanged(nameof(SelectedNestedBotEditableName));
        }
    }

    /// <summary>The referenced bot's name (or a placeholder) — read-only display.</summary>
    public string SelectedNestedBotName =>
        Node is null ? string.Empty : NestedBotCardInfo.Resolve(Node.Config, _editor.NestedBotLibrary);

    /// <summary>Two-way name of the selected entry: setting it renames the library entry live (and every card
    /// that references it). Empty/whitespace is ignored.</summary>
    public string SelectedNestedBotEditableName
    {
        get => SelectedNestedBotId is Guid id ? (_editor.NestedBotLibrary.Get(id)?.Name ?? string.Empty) : string.Empty;
        set
        {
            if (SelectedNestedBotId is Guid id && !string.IsNullOrWhiteSpace(value))
            {
                _editor.NestedBotLibrary.Rename(id, value);
                _editor.RefreshNestedBotSubtitles();
                OnPropertyChanged(nameof(SelectedNestedBotName));
                OnPropertyChanged(nameof(NestedBotEntries));
            }
        }
    }

    /// <summary>Imports an external bot as a new library entry and assigns it to the selected card.</summary>
    public Bot ImportNestedBot(Bot external)
    {
        var entry = _editor.NestedBotLibrary.Import(external);
        SelectedNestedBotId = entry.Id; // assigns + refreshes subtitle
        OnPropertyChanged(nameof(NestedBotEntries));
        OnPropertyChanged(nameof(SelectedNestedBotEditableName));
        return entry;
    }

    /// <summary>Removes the selected entry from the library and unassigns the card.</summary>
    public void RemoveSelectedNestedBot()
    {
        if (SelectedNestedBotId is Guid id)
        {
            _editor.NestedBotLibrary.Remove(id);
            SelectedNestedBotId = null;
            OnPropertyChanged(nameof(NestedBotEntries));
        }
    }
```
- In `Rebuild()`, add to the `OnPropertyChanged(...)` block at the end:
```csharp
        OnPropertyChanged(nameof(IsNestedBotCard));
        OnPropertyChanged(nameof(NestedBotEntries));
        OnPropertyChanged(nameof(SelectedNestedBotId));
        OnPropertyChanged(nameof(SelectedNestedBotName));
        OnPropertyChanged(nameof(SelectedNestedBotEditableName));
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ADB.slnx --filter "FullyQualifiedName~NestedBotPropertiesTests"`
Expected: PASS (6).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Properties/PropertiesViewModel.cs BotBuilder.Core.Tests/Properties/NestedBotPropertiesTests.cs
git commit -m "PropertiesViewModel: nested-bot picker/import/rename/remove members"
```

---

### Task 3: WPF — distinct card visual + properties-panel section

**Files:**
- Modify: `BotBuilder/MainWindow.xaml`
- Modify: `BotBuilder/MainWindow.xaml.cs`

Read the current node `DataTemplate` (the `NodeHost` `ItemsControl.ItemTemplate`, ~lines 260-316) and the properties panel (`DataContext="{Binding Properties}"` `StackPanel`, ~lines 350-380) before editing, and follow their existing structure/converters (`NullToCollapsedConverter`, `BoolToVis`, etc.).

- [ ] **Step 1: Card visual — subtitle line + distinct boxed border**

In `BotBuilder/MainWindow.xaml`, in the node card `DataTemplate`:
- Add a subtitle `TextBlock` directly under the existing `TargetBadge` `TextBlock` inside the header `StackPanel` (it shows the nested bot name; collapses when null for non-nested nodes):
```xml
<TextBlock Text="{Binding Subtitle}" FontSize="11" FontStyle="Italic"
           Foreground="{DynamicResource SecondaryTextBrush}" Margin="6,0,6,3"
           TextTrimming="CharacterEllipsis"
           Visibility="{Binding Subtitle, Converter={x:Static local:NullToCollapsedConverter.Instance}}" />
```
- Make the card border distinct for nested bots: on the card root `Border`, add a `Style` with a `DataTrigger` on `TypeKey` so nested-bot cards get a thicker, accented (double-look) border. Add to the card `Border` a `Style` (preserving the existing selection/run-state `BorderBrush` MultiBinding — keep that as-is; only add a trigger that bumps `BorderThickness` for the nested type):
```xml
<Border.Style>
    <Style TargetType="Border">
        <Setter Property="BorderThickness" Value="2" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding TypeKey}" Value="control.nestedBot">
                <Setter Property="BorderThickness" Value="3" />
            </DataTrigger>
        </Style.Triggers>
    </Style>
</Border.Style>
```
(If the existing `Border` already sets `BorderThickness="2"` inline, remove that inline attribute so the Style governs it — an inline value would otherwise win over the Style setter.) Keep the existing `Border.BorderBrush` MultiBinding untouched.
- In the header `Border` (the colored title bar), append a small nested glyph after the label so the card reads as a container. Inside the header, change the single label `TextBlock` to a `DockPanel` with the label plus a right-aligned glyph shown only for nested bots:
```xml
<DockPanel>
    <TextBlock DockPanel.Dock="Right" Text="&#x29C9;" Foreground="White" FontWeight="Bold" Margin="4,0,0,0"
               ToolTip="Nested bot"
               Visibility="{Binding TypeKey, Converter={x:Static local:NestedBotGlyphVisibilityConverter.Instance}}" />
    <TextBlock Text="{Binding Label}" Foreground="White" FontWeight="Bold" />
</DockPanel>
```
(`&#x29C9;` is ⧉, a vector glyph — no color emoji.)

- [ ] **Step 2: A tiny converter for the glyph**

In `BotBuilder/ValueConverters.cs` (where `NullToCollapsedConverter` etc. live), add a converter that shows the glyph only for the nested-bot TypeKey:
```csharp
public sealed class NestedBotGlyphVisibilityConverter : IValueConverter
{
    public static readonly NestedBotGlyphVisibilityConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value as string == AdbCore.Actions.BuiltIn.NestedBotAction.NestedBotTypeKey
            ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```
(Match the file's existing converter style — `using System.Globalization; using System.Windows; using System.Windows.Data;`. If `ValueConverters.cs` uses a different namespace/pattern for the singletons, mirror it exactly.)

- [ ] **Step 3: Properties-panel nested-bot section**

In `BotBuilder/MainWindow.xaml`, in the properties `StackPanel` (`DataContext="{Binding Properties}"`), add a nested-bot section that is visible only when `IsNestedBotCard`. Place it after the Target combo / before the generic `Fields` `ItemsControl` so the picker sits above the three flag checkboxes (which render from `Fields`). The three flags need no markup — they come from the generic `Fields` rendering:
```xml
<StackPanel Margin="0,4,0,8" Visibility="{Binding IsNestedBotCard, Converter={StaticResource BoolToVis}}">
    <TextBlock Text="Nested bot" FontSize="11" Foreground="{DynamicResource SecondaryTextBrush}" />
    <ComboBox ItemsSource="{Binding NestedBotEntries}" DisplayMemberPath="Name"
              SelectedValuePath="Id" SelectedValue="{Binding SelectedNestedBotId}"
              Margin="0,0,0,4" ToolTip="The library bot this card runs" />
    <TextBlock Text="Name" FontSize="11" Foreground="{DynamicResource SecondaryTextBrush}" />
    <TextBox Text="{Binding SelectedNestedBotEditableName, UpdateSourceTrigger=LostFocus}" Margin="0,0,0,4"
             ToolTip="Rename this nested bot (updates every card that uses it)" />
    <StackPanel Orientation="Horizontal">
        <Button Content="Import .bot…" Click="ImportNestedBot_Click" Padding="6,2" Margin="0,0,4,0" />
        <Button Content="Remove" Click="RemoveNestedBot_Click" Padding="6,2" />
    </StackPanel>
</StackPanel>
```
(`BoolToVis` is the existing `BooleanToVisibilityConverter` resource. The ComboBox inherits the themed template from `Controls.xaml`.)

- [ ] **Step 4: Code-behind handlers**

In `BotBuilder/MainWindow.xaml.cs`, add:
```csharp
    private void ImportNestedBot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = BotFilter };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        AdbCore.Models.Bot external;
        try
        {
            external = new AdbCore.Serialization.BotSerializer().Load(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't import that .bot file: {ex.Message}", "Import nested bot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _editor.Properties.ImportNestedBot(external);
    }

    private void RemoveNestedBot_Click(object sender, RoutedEventArgs e)
        => _editor.Properties.RemoveSelectedNestedBot();
```
(`OpenFileDialog`/`BotFilter`/`MessageBox` are already used in this file.)

- [ ] **Step 5: Build + manual verification**

Run: `dotnet build ADB.slnx` — Build succeeded.

Launch `dotnet run --project BotBuilder`. Drop a "Nested Bot" card (Control Flow palette) — it should render with the distinct cyan accent, a 3px boxed border, the ⧉ glyph, and a "(no bot assigned)" subtitle. Select it: the properties panel shows the Nested bot section (dropdown + Name + Import/Remove) plus the three Send/Receive checkboxes. Import a small saved `.bot` → the card subtitle shows its name; rename via the Name box → subtitle + dropdown update. Verify Light/Dark/HighContrast all render the section and card correctly (the ComboBox dropdown themed).

- [ ] **Step 6: Commit**

```bash
git add BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs BotBuilder/ValueConverters.cs
git commit -m "Nested Bot card visual + properties-panel picker/import/rename/remove"
```

---

### Task 4: Full suite green

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test ADB.slnx`
Expected: PASS, no regressions.

---

## Self-Review

- **Spec coverage (B3b):** distinct card (accent + boxed border + on-card name + glyph + empty/missing states) — Tasks 1/3; properties picker (dropdown), import `.bot`, live rename, remove, three flags (via generic Fields) — Tasks 2/3; theming via DynamicResource + inherited ComboBox template — Task 3. ✓
- **Deferred to B3c (not here):** New-empty, double-click-to-edit, modeless child editor, edit-time cycle guard on assign. ✓
- **Placeholders:** none — full code for VM + XAML. ✓
- **Type consistency:** `CategoryColors.NestedBot`, `NodeViewModel.Subtitle`, `NestedBotCardInfo.Resolve/Unassigned/Missing`, `BotEditorViewModel.RefreshNestedBotSubtitles`, and the `PropertiesViewModel` members are referenced identically across tasks. ✓
- **Notes for executor:** read the live node `DataTemplate` and properties `StackPanel` before editing (Task 3) and adapt anchors to the actual markup; confirm `ValueConverters.cs` namespace/pattern before adding the converter; if the card `Border` sets `BorderThickness` inline, move it into the new Style so the trigger governs it.
