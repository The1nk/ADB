# M8a — Builder Test Run + Target Picker + Log Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a **Run → Test Run** flow to BotBuilder: a target-picker dialog (live window candidates + an editable selector + the equivalent CLI command), which spawns `BotRunner.exe` on a temp `.bot` and tails its JSON-lines log in a new bottom panel with a Stop button.

**Architecture:** All decision logic goes in `BotBuilder.Core/Integration/` (unit-tested): `RunCommandBuilder` (targets → args + display command), `RunnerLogParser`/`RunLogEntry` (JSON line → display model), `ExeLocator` (find the sibling exe), and `TargetPickerViewModel` (editable per-target selectors + live CLI preview). The WPF shell adds the dialog, a `RunSession` (process spawn + async stdout pump), a bottom log panel, and the Run menu.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm, System.Text.Json, xUnit. Reuses the runner's JSON-lines protocol and `IWindowEnumerator`. Per `Docs/Specs/2026-06-03-m8-integration-design.md` §3.

---

## File Structure

**BotBuilder.Core/Integration (new, tested):**
- `RunCommandBuilder.cs` — `BuildArgs` (List<string> for Process) + `BuildDisplayCommand` (quoted copy-paste string).
- `RunLogEntry.cs` — display model + `RunLogKind` enum + `Display` rendering.
- `RunnerLogParser.cs` — `Parse(string line)` → `RunLogEntry` (malformed → Unparsed).
- `ExeLocator.cs` — `Locate(candidates, exists)` + `Candidates(baseDir, exeFileName)`.
- `TargetPickerViewModel.cs` + `TargetSelectionRow.cs` — editable selectors + `CommandPreview` + `Selectors()`.

**BotBuilder (WPF, new):**
- `TargetPickerDialog.xaml(.cs)` — the dialog (window dropdowns + CLI preview + Copy/Run/Cancel).
- `RunSession.cs` — wraps `Process`, async stdout → parsed `RunLogEntry` events on the UI thread, `Stop()`.
- `LogPanelView.xaml(.cs)` — the bottom log list + Stop button.

**BotBuilder (WPF, modified):**
- `MainWindow.xaml` — Run menu + a collapsible bottom log row.
- `MainWindow.xaml.cs` — `TestRun_Click` (temp-bot save → dialog → RunSession → panel).

**Tests:** `BotBuilder.Core.Tests/Integration/RunCommandBuilderTests.cs`, `RunnerLogParserTests.cs`, `ExeLocatorTests.cs`, `TargetPickerViewModelTests.cs`.

---

## Task 1: RunCommandBuilder

**Files:**
- Create: `BotBuilder.Core/Integration/RunCommandBuilder.cs`
- Test: `BotBuilder.Core.Tests/Integration/RunCommandBuilderTests.cs`

- [ ] **Step 1: Write the failing tests** — `BotBuilder.Core.Tests/Integration/RunCommandBuilderTests.cs`:

```csharp
using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class RunCommandBuilderTests
{
    [Fact]
    public void BuildArgs_BotAndTargets_ProducesFlagList()
    {
        var args = RunCommandBuilder.BuildArgs(
            @"C:\bots\farm.bot",
            new[] { ("Client 1", "process:BlueStacks"), ("My Phone", "serial:emulator-5554") });

        Assert.Equal(
            new[] { "--bot", @"C:\bots\farm.bot",
                    "--target", "Client 1=process:BlueStacks",
                    "--target", "My Phone=serial:emulator-5554" },
            args);
    }

    [Fact]
    public void BuildDisplayCommand_QuotesValuesWithSpaces()
    {
        var cmd = RunCommandBuilder.BuildDisplayCommand(
            "BotRunner.exe",
            @"C:\my bots\farm.bot",
            new[] { ("Client 1", "process:BlueStacks") });

        Assert.Equal(
            "BotRunner.exe --bot \"C:\\my bots\\farm.bot\" --target \"Client 1=process:BlueStacks\"",
            cmd);
    }

    [Fact]
    public void BuildDisplayCommand_NoTargets_JustBot()
    {
        var cmd = RunCommandBuilder.BuildDisplayCommand("BotRunner.exe", @"C:\farm.bot", Array.Empty<(string, string)>());
        Assert.Equal("BotRunner.exe --bot C:\\farm.bot", cmd);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~RunCommandBuilderTests"` → FAIL (type missing).

- [ ] **Step 3: Implement** — `BotBuilder.Core/Integration/RunCommandBuilder.cs`:

```csharp
using System.Text;

namespace BotBuilder.Core.Integration;

/// <summary>Builds the BotRunner invocation from a bot file path and per-target selectors: an argument
/// list for <c>Process.Start</c>, and a quoted, copy-pasteable command string for the dialog preview.</summary>
public static class RunCommandBuilder
{
    public static IReadOnlyList<string> BuildArgs(
        string botPath, IReadOnlyList<(string Name, string Selector)> targets)
    {
        var args = new List<string> { "--bot", botPath };
        foreach (var (name, selector) in targets)
        {
            args.Add("--target");
            args.Add($"{name}={selector}");
        }

        return args;
    }

    public static string BuildDisplayCommand(
        string exeName, string botPath, IReadOnlyList<(string Name, string Selector)> targets)
    {
        var sb = new StringBuilder(exeName);
        sb.Append(" --bot ").Append(Quote(botPath));
        foreach (var (name, selector) in targets)
        {
            sb.Append(" --target ").Append(Quote($"{name}={selector}"));
        }

        return sb.ToString();
    }

    // Quote a token only when it contains whitespace (or is empty); escape embedded quotes.
    private static string Quote(string value)
        => value.Length == 0 || value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
}
```

- [ ] **Step 4: Run to verify they pass** — same filter → PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Integration/RunCommandBuilder.cs BotBuilder.Core.Tests/Integration/RunCommandBuilderTests.cs
git commit -m "feat(builder): add RunCommandBuilder (runner args + CLI-preview command)"
```

---

## Task 2: RunLogEntry + RunnerLogParser

**Files:**
- Create: `BotBuilder.Core/Integration/RunLogEntry.cs`, `BotBuilder.Core/Integration/RunnerLogParser.cs`
- Test: `BotBuilder.Core.Tests/Integration/RunnerLogParserTests.cs`

> The runner writes JSON-lines with **camelCase** keys (`event`, `actionId`, `label`, `success`, `error`,
> `message`). The parser is case-insensitive and turns any blank/non-JSON line into an `Unparsed` entry
> (the runner can emit non-JSON if it crashes — never throw on a bad line).

- [ ] **Step 1: Write the failing tests** — `BotBuilder.Core.Tests/Integration/RunnerLogParserTests.cs`:

```csharp
using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class RunnerLogParserTests
{
    [Fact]
    public void Parse_RunStart()
    {
        var e = RunnerLogParser.Parse("""{"event":"run-start","bot":"Farm"}""");
        Assert.Equal(RunLogKind.RunStart, e.Kind);
        Assert.Equal("▶ run started", e.Display);
    }

    [Fact]
    public void Parse_ActionSuccess_ShowsLabel()
    {
        var e = RunnerLogParser.Parse("""{"event":"action","actionId":"a1","label":"Find Attack","success":true}""");
        Assert.Equal(RunLogKind.Action, e.Kind);
        Assert.Equal("a1", e.ActionId);
        Assert.True(e.Success);
        Assert.Equal("✓ Find Attack", e.Display);
    }

    [Fact]
    public void Parse_ActionFailure_ShowsError()
    {
        var e = RunnerLogParser.Parse("""{"event":"action","actionId":"a2","label":"Tap","success":false,"error":"no match"}""");
        Assert.Equal(RunLogKind.Action, e.Kind);
        Assert.False(e.Success);
        Assert.Equal("✗ Tap: no match", e.Display);
    }

    [Fact]
    public void Parse_RunEndFailure()
    {
        var e = RunnerLogParser.Parse("""{"event":"run-end","success":false,"error":"halted"}""");
        Assert.Equal(RunLogKind.RunEnd, e.Kind);
        Assert.Equal("■ run failed: halted", e.Display);
    }

    [Fact]
    public void Parse_LogMessage()
    {
        var e = RunnerLogParser.Parse("""{"event":"log","message":"hello"}""");
        Assert.Equal(RunLogKind.Message, e.Kind);
        Assert.Equal("hello", e.Display);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ broken json")]
    public void Parse_Garbage_IsUnparsed_NotThrown(string line)
    {
        var e = RunnerLogParser.Parse(line);
        Assert.Equal(RunLogKind.Unparsed, e.Kind);
        Assert.Equal(line, e.Display); // raw passthrough
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~RunnerLogParserTests"` → FAIL.

- [ ] **Step 3: Implement `RunLogEntry`** — `BotBuilder.Core/Integration/RunLogEntry.cs`:

```csharp
namespace BotBuilder.Core.Integration;

/// <summary>The kind of a parsed runner log line.</summary>
public enum RunLogKind { RunStart, Action, Message, RunEnd, Unparsed }

/// <summary>A parsed runner JSON-lines record in display form.</summary>
public sealed record RunLogEntry(
    RunLogKind Kind,
    string? ActionId,
    string? Label,
    bool? Success,
    string? Error,
    string? Message,
    string Raw)
{
    /// <summary>A friendly one-line rendering for the log panel.</summary>
    public string Display => Kind switch
    {
        RunLogKind.RunStart => "▶ run started",
        RunLogKind.Action => (Success == true ? "✓ " : "✗ ")
                             + (Label ?? ActionId ?? "action")
                             + (Success == false && !string.IsNullOrEmpty(Error) ? $": {Error}" : string.Empty),
        RunLogKind.Message => Message ?? string.Empty,
        RunLogKind.RunEnd => Success == true
            ? "■ run succeeded"
            : "■ run failed" + (!string.IsNullOrEmpty(Error) ? $": {Error}" : string.Empty),
        _ => Raw,
    };
}
```

- [ ] **Step 4: Implement `RunnerLogParser`** — `BotBuilder.Core/Integration/RunnerLogParser.cs`:

```csharp
using System.Text.Json;

namespace BotBuilder.Core.Integration;

/// <summary>Parses one runner JSON-lines record into a <see cref="RunLogEntry"/>. Tolerant: a blank or
/// non-JSON line becomes an <see cref="RunLogKind.Unparsed"/> entry rather than throwing (the runner can
/// emit non-JSON text if it crashes).</summary>
public static class RunnerLogParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static RunLogEntry Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Unparsed(line);
        }

        try
        {
            var raw = JsonSerializer.Deserialize<RawLine>(line, Options);
            if (raw is null)
            {
                return Unparsed(line);
            }

            var kind = raw.Event switch
            {
                "run-start" => RunLogKind.RunStart,
                "action" => RunLogKind.Action,
                "log" => RunLogKind.Message,
                "run-end" => RunLogKind.RunEnd,
                _ => RunLogKind.Unparsed,
            };

            return new RunLogEntry(kind, raw.ActionId, raw.Label, raw.Success, raw.Error, raw.Message, line);
        }
        catch (JsonException)
        {
            return Unparsed(line);
        }
    }

    private static RunLogEntry Unparsed(string line)
        => new(RunLogKind.Unparsed, null, null, null, null, null, line);

    private sealed record RawLine
    {
        public string? Event { get; init; }
        public string? ActionId { get; init; }
        public string? Label { get; init; }
        public bool? Success { get; init; }
        public string? Error { get; init; }
        public string? Message { get; init; }
    }
}
```

- [ ] **Step 5: Run to verify they pass** — same filter → PASS (9 tests, counting the theory cases).

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/Integration/RunLogEntry.cs BotBuilder.Core/Integration/RunnerLogParser.cs BotBuilder.Core.Tests/Integration/RunnerLogParserTests.cs
git commit -m "feat(builder): add RunnerLogParser + RunLogEntry (JSON-lines -> display model)"
```

---

## Task 3: ExeLocator

**Files:**
- Create: `BotBuilder.Core/Integration/ExeLocator.cs`
- Test: `BotBuilder.Core.Tests/Integration/ExeLocatorTests.cs`

- [ ] **Step 1: Write the failing tests** — `BotBuilder.Core.Tests/Integration/ExeLocatorTests.cs`:

```csharp
using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class ExeLocatorTests
{
    [Fact]
    public void Locate_ReturnsFirstThatExists()
    {
        var found = ExeLocator.Locate(
            new[] { @"C:\a\BotRunner.exe", @"C:\b\BotRunner.exe" },
            exists: p => p == @"C:\b\BotRunner.exe");

        Assert.Equal(@"C:\b\BotRunner.exe", found);
    }

    [Fact]
    public void Locate_NoneExist_ReturnsNull()
    {
        Assert.Null(ExeLocator.Locate(new[] { @"C:\a\x.exe" }, exists: _ => false));
    }

    [Fact]
    public void Candidates_IncludesSiblingAndDevSibling()
    {
        var c = ExeLocator.Candidates(
            baseDir: @"C:\src\ADB\BotBuilder\bin\Debug\net10.0-windows",
            exeFileName: "BotRunner.exe");

        // Deployed: exe next to the Builder.
        Assert.Contains(@"C:\src\ADB\BotBuilder\bin\Debug\net10.0-windows\BotRunner.exe", c);
        // Dev: same bin/<cfg>/<tfm> layout under the sibling project.
        Assert.Contains(@"C:\src\ADB\BotRunner\bin\Debug\net10.0-windows\BotRunner.exe", c);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~ExeLocatorTests"` → FAIL.

- [ ] **Step 3: Implement** — `BotBuilder.Core/Integration/ExeLocator.cs`:

```csharp
namespace BotBuilder.Core.Integration;

/// <summary>Locates a sibling executable (e.g. <c>BotRunner.exe</c>) by probing candidate paths. The
/// selection (<see cref="Locate"/>) is pure for testing; the runtime caller passes
/// <c>File.Exists</c> and <see cref="Candidates"/> built from <c>AppContext.BaseDirectory</c>.</summary>
public static class ExeLocator
{
    /// <summary>The first candidate that satisfies <paramref name="exists"/>, or null when none do.</summary>
    public static string? Locate(IEnumerable<string> candidatePaths, Func<string, bool> exists)
        => candidatePaths.FirstOrDefault(exists);

    /// <summary>Candidate paths for a sibling exe: next to the Builder (deployed), and the dev build-output
    /// sibling that shares the same <c>bin/&lt;config&gt;/&lt;tfm&gt;</c> layout under its own project folder.</summary>
    public static IReadOnlyList<string> Candidates(string baseDir, string exeFileName)
    {
        var sep = Path.DirectorySeparatorChar;
        var deployed = Path.Combine(baseDir, exeFileName);

        // Dev: ...\<root>\BotBuilder\bin\<cfg>\<tfm>\  ->  ...\<root>\<ExeProject>\bin\<cfg>\<tfm>\<exe>
        var project = Path.GetFileNameWithoutExtension(exeFileName);
        var devDir = baseDir.Replace($"{sep}BotBuilder{sep}", $"{sep}{project}{sep}");
        var dev = Path.Combine(devDir, exeFileName);

        return new[] { deployed, dev };
    }
}
```

- [ ] **Step 4: Run to verify they pass** — same filter → PASS (3 tests).

> Note: the `Candidates` test uses Windows-style paths; on the test host `Path.DirectorySeparatorChar` is
> `\`, so the literal `C:\...` expectations match. (The project only targets `net10.0-windows`.)

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Integration/ExeLocator.cs BotBuilder.Core.Tests/Integration/ExeLocatorTests.cs
git commit -m "feat(builder): add ExeLocator (find sibling BotRunner/BotCapture exe)"
```

---

## Task 4: TargetPickerViewModel + TargetSelectionRow

**Files:**
- Create: `BotBuilder.Core/Integration/TargetSelectionRow.cs`, `BotBuilder.Core/Integration/TargetPickerViewModel.cs`
- Test: `BotBuilder.Core.Tests/Integration/TargetPickerViewModelTests.cs`

- [ ] **Step 1: Write the failing tests** — `BotBuilder.Core.Tests/Integration/TargetPickerViewModelTests.cs`:

```csharp
using AdbCore.Models;
using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class TargetPickerViewModelTests
{
    private static TargetPickerViewModel Make() => new(
        "BotRunner.exe",
        @"C:\farm.bot",
        new[]
        {
            ("Client 1", BotTargetType.Window, "process:BlueStacks"),
            ("My Phone", BotTargetType.AndroidDevice, "serial:emulator-5554"),
        });

    [Fact]
    public void Rows_MirrorTargets_WithWindowFlag()
    {
        var vm = Make();

        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("Client 1", vm.Rows[0].Name);
        Assert.True(vm.Rows[0].IsWindow);
        Assert.False(vm.Rows[1].IsWindow);
        Assert.Equal("serial:emulator-5554", vm.Rows[1].Selector);
    }

    [Fact]
    public void CommandPreview_ReflectsCurrentSelectors()
    {
        var vm = Make();

        Assert.Equal(
            "BotRunner.exe --bot C:\\farm.bot --target \"Client 1=process:BlueStacks\" --target \"My Phone=serial:emulator-5554\"",
            vm.CommandPreview);
    }

    [Fact]
    public void EditingSelector_RaisesCommandPreviewChange()
    {
        var vm = Make();
        var changed = false;
        vm.PropertyChanged += (_, e) => changed |= e.PropertyName == nameof(vm.CommandPreview);

        vm.Rows[0].Selector = "hwnd:12345";

        Assert.True(changed);
        Assert.Contains("Client 1=hwnd:12345", vm.CommandPreview);
    }

    [Fact]
    public void Selectors_ReturnsNameSelectorPairs()
    {
        var vm = Make();
        var pairs = vm.Selectors();

        Assert.Equal(("Client 1", "process:BlueStacks"), pairs[0]);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~TargetPickerViewModelTests"` → FAIL.

- [ ] **Step 3: Implement `TargetSelectionRow`** — `BotBuilder.Core/Integration/TargetSelectionRow.cs`:

```csharp
using AdbCore.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Integration;

/// <summary>One editable row in the target picker: the declared target's name/type plus the user's
/// chosen selector. <see cref="IsWindow"/> drives whether the row shows a live window dropdown.</summary>
public partial class TargetSelectionRow : ObservableObject
{
    public TargetSelectionRow(string name, BotTargetType type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }
    public BotTargetType Type { get; }
    public bool IsWindow => Type == BotTargetType.Window;

    [ObservableProperty] private string _selector = string.Empty;
}
```

- [ ] **Step 4: Implement `TargetPickerViewModel`** — `BotBuilder.Core/Integration/TargetPickerViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using AdbCore.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Integration;

/// <summary>Drives the target-picker dialog: one editable <see cref="TargetSelectionRow"/> per declared
/// target, and a live <see cref="CommandPreview"/> of the equivalent BotRunner command.</summary>
public partial class TargetPickerViewModel : ObservableObject
{
    private readonly string _exeName;
    private readonly string _botPath;

    public TargetPickerViewModel(
        string exeName, string botPath, IEnumerable<(string Name, BotTargetType Type, string Selector)> targets)
    {
        _exeName = exeName;
        _botPath = botPath;

        Rows = new ObservableCollection<TargetSelectionRow>();
        foreach (var (name, type, selector) in targets)
        {
            var row = new TargetSelectionRow(name, type) { Selector = selector };
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }
    }

    public ObservableCollection<TargetSelectionRow> Rows { get; }

    /// <summary>The copy-pasteable equivalent command, recomputed as selectors change.</summary>
    public string CommandPreview =>
        RunCommandBuilder.BuildDisplayCommand(_exeName, _botPath, PairList());

    /// <summary>The (name, selector) pairs to hand to <see cref="RunCommandBuilder.BuildArgs"/>.</summary>
    public IReadOnlyList<(string Name, string Selector)> Selectors() => PairList();

    private List<(string Name, string Selector)> PairList()
        => Rows.Select(r => (r.Name, r.Selector)).ToList();

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetSelectionRow.Selector))
        {
            OnPropertyChanged(nameof(CommandPreview));
        }
    }
}
```

- [ ] **Step 5: Run to verify they pass** — same filter → PASS (4 tests). Then `dotnet build BotBuilder.Core/BotBuilder.Core.csproj -c Debug --nologo` → 0 warnings (`_selector` is the only `[ObservableProperty]`, accessed via its generated property).

- [ ] **Step 6: Commit**

```bash
git add BotBuilder.Core/Integration/TargetSelectionRow.cs BotBuilder.Core/Integration/TargetPickerViewModel.cs BotBuilder.Core.Tests/Integration/TargetPickerViewModelTests.cs
git commit -m "feat(builder): add TargetPickerViewModel (editable selectors + live CLI preview)"
```

---

## Task 5: TargetPickerDialog + RunSession (WPF, visual)

No unit tests — verified by building and (later) running. The dialog binds `TargetPickerViewModel`; `RunSession` wraps the runner process and pumps stdout into parsed `RunLogEntry` events on the UI thread.

**Files:**
- Create: `BotBuilder/TargetPickerDialog.xaml`, `BotBuilder/TargetPickerDialog.xaml.cs`, `BotBuilder/RunSession.cs`

- [ ] **Step 1: Create `RunSession.cs`** — `BotBuilder/RunSession.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using System.Threading;
using BotBuilder.Core.Integration;

namespace BotBuilder;

/// <summary>Owns a running BotRunner process: pumps its stdout lines (parsed into <see cref="RunLogEntry"/>)
/// and its exit code back to the caller on the captured UI <see cref="SynchronizationContext"/>.</summary>
public sealed class RunSession
{
    private readonly Process _process;
    private readonly SynchronizationContext _sync;

    private RunSession(Process process, SynchronizationContext sync)
    {
        _process = process;
        _sync = sync;
    }

    public event EventHandler<RunLogEntry>? EntryReceived;
    public event EventHandler<int>? Exited;

    public static RunSession Start(string exePath, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var session = new RunSession(process, SynchronizationContext.Current ?? new SynchronizationContext());

        process.OutputDataReceived += (_, e) => session.OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => session.OnLine(e.Data); // runner errors arrive as plain text -> Unparsed
        process.Exited += (_, _) => session.Post(() => session.Exited?.Invoke(session, process.ExitCode));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return session;
    }

    /// <summary>Kills the run if it is still going.</summary>
    public void Stop()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) { /* already exited */ }
    }

    private void OnLine(string? line)
    {
        if (line is null)
        {
            return; // end-of-stream marker
        }

        var entry = RunnerLogParser.Parse(line);
        Post(() => EntryReceived?.Invoke(this, entry));
    }

    private void Post(Action action) => _sync.Post(_ => action(), null);
}
```

- [ ] **Step 2: Create `TargetPickerDialog.xaml`** — `BotBuilder/TargetPickerDialog.xaml`:

```xml
<Window x:Class="BotBuilder.TargetPickerDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Test Run — choose targets" Height="420" Width="720"
        WindowStartupLocation="CenterOwner">
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis" />
    </Window.Resources>
    <DockPanel Margin="12">
        <StackPanel DockPanel.Dock="Bottom" Margin="0,12,0,0">
            <TextBlock Text="Equivalent command (copy for scripts / Task Scheduler):" Foreground="Gray" />
            <DockPanel Margin="0,2,0,8">
                <Button DockPanel.Dock="Right" Content="Copy" Width="64" Margin="8,0,0,0" Click="OnCopy" />
                <TextBox x:Name="CommandText" Text="{Binding CommandPreview, Mode=OneWay}" IsReadOnly="True"
                         TextWrapping="Wrap" />
            </DockPanel>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Content="Run" Width="90" Click="OnRun" IsDefault="True" />
                <Button Content="Cancel" Width="90" Margin="8,0,0,0" Click="OnCancel" IsCancel="True" />
            </StackPanel>
        </StackPanel>
        <ItemsControl ItemsSource="{Binding Rows}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Grid Margin="0,4">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="140" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0" VerticalAlignment="Center">
                            <TextBlock Text="{Binding Name}" FontWeight="SemiBold" />
                            <TextBlock Text="{Binding Type}" Foreground="Gray" FontSize="11" />
                        </StackPanel>
                        <TextBox Grid.Column="1" VerticalAlignment="Center"
                                 Text="{Binding Selector, UpdateSourceTrigger=PropertyChanged}" />
                        <!-- Window targets get a live dropdown; populated + handled in code-behind. -->
                        <ComboBox Grid.Column="2" Width="180" Margin="8,0,0,0" VerticalAlignment="Center"
                                  Visibility="{Binding IsWindow, Converter={StaticResource BoolToVis}}"
                                  Tag="{Binding}" Loaded="OnWindowComboLoaded" SelectionChanged="OnWindowChosen"
                                  DisplayMemberPath="Display" />
                    </Grid>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </DockPanel>
</Window>
```

- [ ] **Step 3: Create `TargetPickerDialog.xaml.cs`** — `BotBuilder/TargetPickerDialog.xaml.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AdbCore.Targets;
using BotBuilder.Core.Integration;

namespace BotBuilder;

public partial class TargetPickerDialog : Window
{
    private readonly TargetPickerViewModel _vm;
    private readonly IWindowEnumerator _windows;

    public TargetPickerDialog(TargetPickerViewModel vm, IWindowEnumerator windows)
    {
        InitializeComponent();
        _vm = vm;
        _windows = windows;
        DataContext = vm;
    }

    /// <summary>The chosen (name, selector) pairs, valid after the dialog returns true.</summary>
    public IReadOnlyList<(string Name, string Selector)> Selectors => _vm.Selectors();

    // A display wrapper for the window dropdown.
    private sealed record WindowChoice(WindowInfo Info)
    {
        public string Display => string.IsNullOrEmpty(Info.ProcessName)
            ? Info.Title
            : $"{Info.ProcessName} — {Info.Title}";
    }

    private void OnWindowComboLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            combo.ItemsSource = _windows.Enumerate().Select(w => new WindowChoice(w)).ToList();
        }
    }

    private void OnWindowChosen(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: WindowChoice choice, Tag: TargetSelectionRow row })
        {
            // Default to a reusable process selector; the user can edit to title:/hwnd:.
            row.Selector = string.IsNullOrEmpty(choice.Info.ProcessName)
                ? $"title:{choice.Info.Title}"
                : $"process:{choice.Info.ProcessName}";
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e) => Clipboard.SetText(CommandText.Text);

    private void OnRun(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

- [ ] **Step 4: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add BotBuilder/RunSession.cs BotBuilder/TargetPickerDialog.xaml BotBuilder/TargetPickerDialog.xaml.cs
git commit -m "feat(builder): target-picker dialog + RunSession (spawn + stdout pump)"
```

---

## Task 6: Log panel + Run menu + MainWindow wiring (WPF, visual)

Ties it together: a Run menu, a collapsible bottom log panel, and `TestRun_Click` that saves a temp bot, opens the dialog, starts a `RunSession`, and streams entries into the panel.

**Files:**
- Create: `BotBuilder/LogPanelView.xaml`, `BotBuilder/LogPanelView.xaml.cs`
- Modify: `BotBuilder/MainWindow.xaml`, `BotBuilder/MainWindow.xaml.cs`

- [ ] **Step 1: Create `LogPanelView.xaml`** — `BotBuilder/LogPanelView.xaml`:

```xml
<UserControl x:Class="BotBuilder.LogPanelView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel>
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Background="#EEE">
            <TextBlock Text="Test Run" FontWeight="SemiBold" Margin="6,3" VerticalAlignment="Center" />
            <TextBlock x:Name="StatusText" Margin="12,3" VerticalAlignment="Center" Foreground="#333" />
            <Button Content="Stop" Click="OnStop" Width="60" Margin="8,2" />
            <Button Content="Clear" Click="OnClear" Width="60" Margin="0,2" />
        </StackPanel>
        <ListBox x:Name="LogList" FontFamily="Consolas" FontSize="12" />
    </DockPanel>
</UserControl>
```

- [ ] **Step 2: Create `LogPanelView.xaml.cs`** — `BotBuilder/LogPanelView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BotBuilder.Core.Integration;

namespace BotBuilder;

public partial class LogPanelView : UserControl
{
    private RunSession? _session;

    public LogPanelView()
    {
        InitializeComponent();
    }

    /// <summary>Attaches a freshly started session and clears the previous run's output.</summary>
    public void Attach(RunSession session)
    {
        _session = session;
        LogList.Items.Clear();
        StatusText.Text = "Running…";
        StatusText.Foreground = Brushes.DimGray;

        session.EntryReceived += (_, entry) => Append(entry);
        session.Exited += (_, code) =>
        {
            StatusText.Text = code == 0 ? "Succeeded" : $"Finished (exit {code})";
            StatusText.Foreground = code == 0 ? Brushes.Green : Brushes.DarkRed;
        };
    }

    private void Append(RunLogEntry entry)
    {
        var item = new ListBoxItem
        {
            Content = entry.Display,
            Foreground = entry.Kind == RunLogKind.Action && entry.Success == false ? Brushes.DarkRed
                       : entry.Kind == RunLogKind.RunEnd && entry.Success == false ? Brushes.DarkRed
                       : Brushes.Black,
        };
        LogList.Items.Add(item);
        LogList.ScrollIntoView(item);
    }

    private void OnStop(object sender, RoutedEventArgs e) => _session?.Stop();

    private void OnClear(object sender, RoutedEventArgs e) => LogList.Items.Clear();
}
```

- [ ] **Step 3: Add the Run menu + bottom log row to `MainWindow.xaml`.** Add a `Run` menu after the `Edit` menu (inside the existing `<Menu>`):

```xml
            <MenuItem Header="_Run">
                <MenuItem Header="_Test Run..." Click="TestRun_Click" InputGestureText="F5" />
            </MenuItem>
```

And host a collapsible log panel docked at the bottom (above the existing `StatusBar`). Wrap the existing
bottom `StatusBar` and add the panel just above it — place this immediately before the `<StatusBar ...>`:

```xml
        <local:LogPanelView x:Name="LogPanel" DockPanel.Dock="Bottom" Height="180"
                            Visibility="Collapsed" />
```

(The `local:` xmlns `clr-namespace:BotBuilder` is already declared on the Window root.)

- [ ] **Step 4: Add `TestRun_Click` + F5 handling to `MainWindow.xaml.cs`.** Add the handler (the class already has `_editor`, a `BotEditorViewModel`):

```csharp
    private void TestRun_Click(object sender, RoutedEventArgs e)
    {
        // 1. Serialize the current editor state to a temp .bot so the run never depends on a saved file.
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "adb-testrun");
        System.IO.Directory.CreateDirectory(dir);
        var botPath = System.IO.Path.Combine(dir, $"{_editor.BotName}.bot");
        _editor.Save(botPath);

        // 2. Locate BotRunner.exe.
        var exe = ResolveRunner();
        if (exe is null)
        {
            MessageBox.Show(
                "BotRunner couldn't be found. Try reinstalling ADB, and check whether your antivirus quarantined it.",
                "Test Run", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 3. Target picker.
        var targets = _editor.TargetBar.Targets
            .Select(t => (t.Name, t.Type, t.Selector))
            .ToList();
        var pickerVm = new BotBuilder.Core.Integration.TargetPickerViewModel(
            System.IO.Path.GetFileName(exe), botPath, targets);
        var dialog = new TargetPickerDialog(pickerVm, new AdbCore.Targets.Win32WindowEnumerator()) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        // 4. Spawn + stream into the log panel.
        var args = BotBuilder.Core.Integration.RunCommandBuilder.BuildArgs(botPath, dialog.Selectors);
        LogPanel.Visibility = Visibility.Visible;
        LogPanel.Attach(RunSession.Start(exe, args));
    }

    private static string? ResolveRunner()
        => BotBuilder.Core.Integration.ExeLocator.Locate(
            BotBuilder.Core.Integration.ExeLocator.Candidates(System.AppContext.BaseDirectory, "BotRunner.exe"),
            System.IO.File.Exists);
```

Then wire **F5**: in the existing `Window_KeyDown` handler in `MainWindow.xaml.cs`, add — when
`e.Key == System.Windows.Input.Key.F5` — a call to `TestRun_Click(this, new RoutedEventArgs())`. (Use the
project's existing `using System.Windows.Input;` if present, or fully-qualify `Key.F5` as shown.)

- [ ] **Step 5: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add BotBuilder/LogPanelView.xaml BotBuilder/LogPanelView.xaml.cs BotBuilder/MainWindow.xaml BotBuilder/MainWindow.xaml.cs
git commit -m "feat(builder): Run menu + bottom log panel; wire Test Run end-to-end"
```

---

## Task 7: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution** — `dotnet build ADB.slnx -c Debug --nologo` → `0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run the whole test suite** — `dotnet test ADB.slnx -c Debug --nologo --no-build`. New BotBuilder.Core tests this slice: RunCommandBuilder 3, RunnerLogParser 9 (incl. theory), ExeLocator 3, TargetPickerViewModel 4 = +19. BotBuilder.Core.Tests should total 122 (was 103). AdbCore 226 / BotCapture.Core 41 / BotRunner 19 unchanged.

- [ ] **Step 3: Manual run (user visual verification).** `dotnet run --project BotBuilder/BotBuilder.csproj -c Debug`:
  - Build a small bot with one Window target, then **Run → Test Run** (or **F5**).
  - The target-picker dialog lists the target; the Window dropdown shows running windows; choosing one fills the selector as `process:<name>`; the equivalent-command box updates live; **Copy** copies it.
  - **Run** opens the bottom log panel and streams `▶ run started`, `✓/✗ <label>` lines, `■ run succeeded/failed`; **Stop** kills a long run; the status shows Succeeded / exit code.
  - Rename `BotRunner.exe` temporarily and Test Run → the friendly "couldn't be found… reinstall / antivirus" message appears (no crash).

> Hand off to the user for visual confirmation before opening the PR.
