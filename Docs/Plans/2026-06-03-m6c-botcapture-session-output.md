# M6c — BotCapture Session List + Integrated `--output` Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete M6 BotCapture: a standalone **session panel** (save folder + saved-capture rows with confidence, re-test 🟢/🔴, delete, click-to-re-edit, "+ New Capture") and an **integrated `--output <path>`** single-shot mode that captures one template to that exact path then exits.

**Architecture:** Add testable Core (`CommandLineArgs`, `SessionRow`, `SessionViewModel`) and reuse the entire M6b capture pipeline (`WindowPickerView` → `RegionSelectView` → `PreviewConfirmView`). `MainWindow` becomes a small navigation host whose **home** is the session panel; "+ New Capture" runs the existing picker→region→confirm flow and, on Save, appends a session row. Re-edit loads a saved PNG (detached so the file stays unlocked) straight into the confirm view. `App.OnStartup` parses args: with `--output` the app skips the session panel, runs one capture flow that saves to the exact path, and exits (0 on save, 1 on cancel).

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm, System.Drawing.Common, xUnit. Reuses AdbCore `IWindowCapture`/`ITemplateMatcher`. Per `Docs/Specs/2026-06-03-m6-botcapture-design.md` §6.

**Design decisions (deliberate):**
- The V1 §7.4 top-level "Source Window" selector is realized **per-row**: each `SessionRow` remembers the window handle it was captured from, so re-test always matches against the correct window (more accurate than a single session-wide source, and the picker already handles window choice).
- Re-edit opens the confirm view directly on the saved image (no region step) with the existing filename + sidecar confidence; Save overwrites the same file and updates the row in place.
- `CommandLineArgs` throws `ArgumentException` on bad input (lighter than BotRunner's dedicated exception type; same parse shape).

---

## File Structure

**BotCapture.Core (new):**
- `CommandLineArgs.cs` — parse `--output <path>`; `OutputPath`/`IsIntegrated`.
- `SessionRow.cs` — saved capture: `FilePath`, `Confidence` (settable/observable), `SourceHandle`, observable `LastRetestMatched`.
- `SessionViewModel.cs` — `Rows`, `SaveFolder`, `Add`/`Remove`/`Retest`.

**BotCapture (WPF, new):**
- `Views/SessionView.xaml(.cs)` — the session panel.
- `Views/RetestIndicatorConverter.cs` — `bool?` → "—"/"🟢"/"🔴".

**BotCapture (WPF, modified):**
- `MainWindow.xaml.cs` — session-home navigation + integrated mode.
- `App.xaml` (remove `StartupUri`) + `App.xaml.cs` (`OnStartup` arg parsing).

**Tests (BotCapture.Core.Tests, new):** `CommandLineArgsTests.cs`, `SessionViewModelTests.cs` (reuses the existing `FakeWindowCapture`/`FakeTemplateMatcher` in `Fakes.cs`).

---

## Task 1: CommandLineArgs

**Files:**
- Create: `BotCapture.Core/CommandLineArgs.cs`
- Test: `BotCapture.Core.Tests/CommandLineArgsTests.cs`

- [ ] **Step 1: Write the failing tests** — `BotCapture.Core.Tests/CommandLineArgsTests.cs`:

```csharp
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class CommandLineArgsTests
{
    [Fact]
    public void NoArgs_IsStandalone()
    {
        var parsed = CommandLineArgs.Parse(Array.Empty<string>());
        Assert.Null(parsed.OutputPath);
        Assert.False(parsed.IsIntegrated);
    }

    [Fact]
    public void Output_SetsPathAndIntegrated()
    {
        var parsed = CommandLineArgs.Parse(new[] { "--output", @"C:\bots\attack.png" });
        Assert.Equal(@"C:\bots\attack.png", parsed.OutputPath);
        Assert.True(parsed.IsIntegrated);
    }

    [Fact]
    public void Output_MissingValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(new[] { "--output" }));
    }

    [Fact]
    public void UnknownArgument_Throws()
    {
        Assert.Throws<ArgumentException>(() => CommandLineArgs.Parse(new[] { "--bogus" }));
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test BotCapture.Core.Tests/BotCapture.Core.Tests.csproj --filter "FullyQualifiedName~CommandLineArgsTests"` → FAIL.

- [ ] **Step 3: Implement** — `BotCapture.Core/CommandLineArgs.cs`:

```csharp
namespace BotCapture.Core;

/// <summary>Parsed BotCapture command-line arguments. With no args the tool runs the standalone session
/// panel; <c>--output &lt;path&gt;</c> runs integrated single-capture mode that saves to that exact path
/// then exits.</summary>
public sealed class CommandLineArgs
{
    public string? OutputPath { get; init; }

    /// <summary>True when launched to capture a single template to <see cref="OutputPath"/> then exit.</summary>
    public bool IsIntegrated => OutputPath is not null;

    public static CommandLineArgs Parse(string[] args)
    {
        string? outputPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("--output requires a value.");
                    }

                    outputPath = args[++i];
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new CommandLineArgs { OutputPath = outputPath };
    }
}
```

- [ ] **Step 4: Run to verify they pass** — same filter → PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add BotCapture.Core/CommandLineArgs.cs BotCapture.Core.Tests/CommandLineArgsTests.cs
git commit -m "feat(capture): add CommandLineArgs (--output integrated-mode parsing)"
```

---

## Task 2: SessionRow + SessionViewModel

**Files:**
- Create: `BotCapture.Core/SessionRow.cs`, `BotCapture.Core/SessionViewModel.cs`
- Test: `BotCapture.Core.Tests/SessionViewModelTests.cs`

- [ ] **Step 1: Write the failing tests** — `BotCapture.Core.Tests/SessionViewModelTests.cs` (reuses `FakeWindowCapture`/`FakeTemplateMatcher` already in `Fakes.cs`):

```csharp
using AdbCore.Screen;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class SessionViewModelTests
{
    private static SessionViewModel Make(FakeWindowCapture capture, FakeTemplateMatcher matcher) =>
        new(capture, matcher, saveFolder: @"C:\bots");

    [Fact]
    public void Add_AppendsRowWithDetails()
    {
        var vm = Make(new FakeWindowCapture(), new FakeTemplateMatcher());

        var row = vm.Add(@"C:\bots\a.png", 0.88, (IntPtr)7);

        Assert.Single(vm.Rows);
        Assert.Same(row, vm.Rows[0]);
        Assert.Equal(@"C:\bots\a.png", row.FilePath);
        Assert.Equal("a.png", row.FileName);
        Assert.Equal(0.88, row.Confidence, 3);
        Assert.Equal((IntPtr)7, row.SourceHandle);
        Assert.Null(row.LastRetestMatched);
    }

    [Fact]
    public void Remove_DropsRow()
    {
        var vm = Make(new FakeWindowCapture(), new FakeTemplateMatcher());
        var row = vm.Add(@"C:\bots\a.png", 0.9, (IntPtr)1);

        vm.Remove(row);

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Retest_Match_SetsGreen_UsesRowHandleAndConfidence()
    {
        var capture = new FakeWindowCapture();
        var matcher = new FakeTemplateMatcher { Next = new MatchResult(0, 0, 4, 4, 0.97) };
        var vm = Make(capture, matcher);
        var row = vm.Add(@"C:\bots\a.png", 0.80, (IntPtr)42);

        vm.Retest(row);

        Assert.True(row.LastRetestMatched);
        Assert.Equal((IntPtr)42, capture.Calls[^1].Handle);
        Assert.Equal(0.80, matcher.LastMinConfidence, 3);
        Assert.Equal(@"C:\bots\a.png", matcher.LastTemplatePath);
    }

    [Fact]
    public void Retest_NoMatch_SetsRed()
    {
        var matcher = new FakeTemplateMatcher { Next = null };
        var vm = Make(new FakeWindowCapture(), matcher);
        var row = vm.Add(@"C:\bots\a.png", 0.95, (IntPtr)1);

        vm.Retest(row);

        Assert.False(row.LastRetestMatched);
    }

    [Fact]
    public void Retest_MatcherThrows_SetsRed_NoException()
    {
        var matcher = new FakeTemplateMatcher { Throw = new FileNotFoundException("gone") };
        var vm = Make(new FakeWindowCapture(), matcher);
        var row = vm.Add(@"C:\bots\a.png", 0.9, (IntPtr)1);

        vm.Retest(row);

        Assert.False(row.LastRetestMatched);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test BotCapture.Core.Tests/BotCapture.Core.Tests.csproj --filter "FullyQualifiedName~SessionViewModelTests"` → FAIL.

- [ ] **Step 3: Implement `SessionRow`** — `BotCapture.Core/SessionRow.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>A capture saved during the current session: its file, the confidence it was saved with, the
/// source window it came from (for re-testing), and the last re-test result (null = not yet tested).</summary>
public partial class SessionRow : ObservableObject
{
    private double _confidence;

    public SessionRow(string filePath, double confidence, IntPtr sourceHandle)
    {
        FilePath = filePath;
        _confidence = confidence;
        SourceHandle = sourceHandle;
    }

    public string FilePath { get; }
    public IntPtr SourceHandle { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>The saved confidence; updatable when the template is re-edited.</summary>
    public double Confidence
    {
        get => _confidence;
        set => SetProperty(ref _confidence, value);
    }

    /// <summary>Last re-test result: null = untested, true = matched at <see cref="Confidence"/>, false = not.</summary>
    [ObservableProperty] private bool? _lastRetestMatched;
}
```

- [ ] **Step 4: Implement `SessionViewModel`** — `BotCapture.Core/SessionViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AdbCore.Screen;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Standalone session state: the captures saved so far, the save folder, and re-testing a saved
/// template against a fresh capture of the window it came from.</summary>
public partial class SessionViewModel : ObservableObject
{
    private readonly IWindowCapture _capture;
    private readonly ITemplateMatcher _matcher;
    private string _saveFolder;

    public SessionViewModel(IWindowCapture capture, ITemplateMatcher matcher, string saveFolder)
    {
        _capture = capture;
        _matcher = matcher;
        _saveFolder = saveFolder;
    }

    public ObservableCollection<SessionRow> Rows { get; } = new();

    /// <summary>The folder new captures are saved into (changeable via the panel's Browse button).</summary>
    public string SaveFolder
    {
        get => _saveFolder;
        set => SetProperty(ref _saveFolder, value);
    }

    /// <summary>Appends a saved capture as a session row and returns it.</summary>
    public SessionRow Add(string filePath, double confidence, IntPtr sourceHandle)
    {
        var row = new SessionRow(filePath, confidence, sourceHandle);
        Rows.Add(row);
        return row;
    }

    public void Remove(SessionRow row) => Rows.Remove(row);

    /// <summary>Re-captures the row's source window and matches its saved template at the row's confidence,
    /// updating <see cref="SessionRow.LastRetestMatched"/> (true = matched). Never throws.</summary>
    public void Retest(SessionRow row)
    {
        try
        {
            using var fresh = _capture.Capture(row.SourceHandle, ScreenCaptureMethod.Auto);
            row.LastRetestMatched = _matcher.Match(fresh, row.FilePath, row.Confidence) is not null;
        }
        catch
        {
            row.LastRetestMatched = false; // missing/unreadable template or capture failure -> red
        }
    }
}
```

- [ ] **Step 5: Run to verify they pass** — filter `~SessionViewModelTests` → PASS (5 tests). Then `dotnet build BotCapture.Core/BotCapture.Core.csproj -c Debug --nologo` → 0 warnings (no MVVMTK0034: `SessionRow.Confidence` and `SessionViewModel.SaveFolder` are manual `SetProperty` properties; `_lastRetestMatched` is the only `[ObservableProperty]` and is accessed solely via its generated property).

- [ ] **Step 6: Commit**

```bash
git add BotCapture.Core/SessionRow.cs BotCapture.Core/SessionViewModel.cs BotCapture.Core.Tests/SessionViewModelTests.cs
git commit -m "feat(capture): add SessionViewModel + SessionRow (rows, add/remove, re-test)"
```

---

## Task 3: SessionView + RetestIndicatorConverter (WPF, visual)

**Files:**
- Create: `BotCapture/Views/RetestIndicatorConverter.cs`, `BotCapture/Views/SessionView.xaml`, `BotCapture/Views/SessionView.xaml.cs`

- [ ] **Step 1: Create the converter** — `BotCapture/Views/RetestIndicatorConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace BotCapture.Views;

/// <summary>Maps a re-test result (bool?) to a status glyph: null = "—", true = "🟢", false = "🔴".</summary>
public sealed class RetestIndicatorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool matched ? (matched ? "🟢" : "🔴") : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Create `SessionView.xaml`**

```xml
<UserControl x:Class="BotCapture.Views.SessionView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:BotCapture.Views">
    <UserControl.Resources>
        <views:RetestIndicatorConverter x:Key="Retest" />
    </UserControl.Resources>
    <DockPanel Margin="12">
        <StackPanel DockPanel.Dock="Top">
            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <TextBlock Text="Save Folder:" VerticalAlignment="Center" Width="80" />
                <TextBox Text="{Binding SaveFolder}" IsReadOnly="True" Width="520" VerticalAlignment="Center" />
                <Button Content="Browse…" Click="OnBrowse" Width="80" Margin="8,0,0,0" />
            </StackPanel>
            <Button Content="+ New Capture" Click="OnNewCapture" HorizontalAlignment="Left" Width="140" Margin="0,0,0,8" />
            <TextBlock Text="Saved this session (double-click a row to re-edit):" Foreground="Gray" Margin="0,0,0,4" />
        </StackPanel>
        <Border BorderBrush="LightGray" BorderThickness="1">
            <ListBox x:Name="RowList" ItemsSource="{Binding Rows}" HorizontalContentAlignment="Stretch"
                     MouseDoubleClick="OnRowDoubleClick">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid Margin="2">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="{Binding FileName}" VerticalAlignment="Center" />
                            <TextBlock Grid.Column="1" Text="{Binding Confidence, StringFormat=F2}" Width="48"
                                       VerticalAlignment="Center" />
                            <TextBlock Grid.Column="2" Width="28" VerticalAlignment="Center" FontSize="14"
                                       Text="{Binding LastRetestMatched, Converter={StaticResource Retest}}" />
                            <Button Grid.Column="3" Content="Re-test" Click="OnRetest" Tag="{Binding}" Margin="4,0,0,0" />
                            <Button Grid.Column="4" Content="🗑" Click="OnDelete" Tag="{Binding}" Margin="4,0,0,0" />
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Border>
    </DockPanel>
</UserControl>
```

- [ ] **Step 3: Create `SessionView.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class SessionView : UserControl
{
    public SessionView()
    {
        InitializeComponent();
    }

    public event EventHandler? NewCaptureRequested;
    public event EventHandler? BrowseFolderRequested;
    public event EventHandler<SessionRow>? RetestRequested;
    public event EventHandler<SessionRow>? DeleteRequested;
    public event EventHandler<SessionRow>? ReEditRequested;

    private void OnNewCapture(object sender, RoutedEventArgs e) => NewCaptureRequested?.Invoke(this, EventArgs.Empty);

    private void OnBrowse(object sender, RoutedEventArgs e) => BrowseFolderRequested?.Invoke(this, EventArgs.Empty);

    private void OnRetest(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SessionRow row)
        {
            RetestRequested?.Invoke(this, row);
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SessionRow row)
        {
            DeleteRequested?.Invoke(this, row);
        }
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowList.SelectedItem is SessionRow row)
        {
            ReEditRequested?.Invoke(this, row);
        }
    }
}
```

- [ ] **Step 4: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add BotCapture/Views/RetestIndicatorConverter.cs BotCapture/Views/SessionView.xaml BotCapture/Views/SessionView.xaml.cs
git commit -m "feat(capture): SessionView panel (folder, rows w/ confidence + re-test/delete)"
```

---

## Task 4: MainWindow session-home navigation (WPF, visual)

Rewrites `MainWindow` so the home screen is the session panel; "+ New Capture" runs the existing picker→region→confirm flow and Save appends a row; rows support re-test, delete, and double-click re-edit. The constructor stays parameterless here; Task 5 adds the integrated `--output` parameter.

**Files:**
- Modify: `BotCapture/MainWindow.xaml.cs`

- [ ] **Step 1: Replace `BotCapture/MainWindow.xaml.cs`** with:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AdbCore.Screen;
using AdbCore.Targets;
using BotCapture.Core;
using BotCapture.Views;

namespace BotCapture;

public partial class MainWindow : Window
{
    private readonly IWindowCapture _capture = new Win32WindowCapture();
    private readonly ITemplateMatcher _matcher = new OpenCvSharpTemplateMatcher();

    private readonly WindowPickerViewModel _pickerVm;
    private readonly WindowPickerView _pickerView;

    private readonly SessionViewModel _sessionVm;
    private readonly SessionView _sessionView;

    private IntPtr _sourceHandle;
    private RegionSelectionViewModel? _regionVm;
    private PreviewConfirmViewModel? _confirmVm;
    private SessionRow? _editingRow;          // non-null while re-editing an existing row
    private readonly string? _outputPath;     // null in standalone; Task 5 sets it for integrated mode

    public MainWindow()
    {
        InitializeComponent();

        _pickerVm = new WindowPickerViewModel(new Win32WindowEnumerator(), _capture);
        _pickerView = new WindowPickerView { DataContext = _pickerVm };
        _pickerView.CaptureAccepted += OnCaptureAccepted;

        _sessionVm = new SessionViewModel(_capture, _matcher, DefaultFolder());
        _sessionView = new SessionView { DataContext = _sessionVm };
        _sessionView.NewCaptureRequested += (_, _) => StartNewCapture();
        _sessionView.RetestRequested += (_, row) => _sessionVm.Retest(row);
        _sessionView.DeleteRequested += (_, row) => _sessionVm.Remove(row);
        _sessionView.ReEditRequested += (_, row) => StartReEdit(row);
        _sessionView.BrowseFolderRequested += (_, _) => BrowseFolder();

        ShowSession();
    }

    private void SetContent(UIElement view)
    {
        Root.Children.Clear();
        Root.Children.Add(view);
    }

    private void ShowSession() => SetContent(_sessionView);

    private void StartNewCapture()
    {
        _editingRow = null;
        _pickerVm.Refresh();
        SetContent(_pickerView);
    }

    private void OnCaptureAccepted(object? sender, EventArgs e)
    {
        var image = _pickerVm.TakeCapturedImage();
        if (image is null || _pickerVm.SelectedWindow is null)
        {
            return;
        }

        _sourceHandle = _pickerVm.SelectedWindow.Info.Handle;
        ShowRegion(new RegionSelectionViewModel(image));
    }

    private void ShowRegion(RegionSelectionViewModel vm)
    {
        _regionVm?.Dispose();
        _regionVm = vm;
        SetContent(BuildRegionView(vm));
    }

    private RegionSelectView BuildRegionView(RegionSelectionViewModel vm)
    {
        var view = new RegionSelectView();
        view.RegionConfirmed += OnRegionConfirmed;
        view.BackRequested += (_, _) => { DisposeRegion(); ReturnHome(); };
        view.Bind(vm);
        return view;
    }

    // Home is the session panel in standalone; in integrated (--output) mode it's the picker, so
    // backing out of region/confirm lets the user re-pick a window rather than land on a stray session.
    private void ReturnHome()
    {
        if (_outputPath is not null)
        {
            SetContent(_pickerView);
        }
        else
        {
            ShowSession();
        }
    }

    private void OnRegionConfirmed(object? sender, System.Drawing.Bitmap crop)
        => ShowConfirm(crop, _sourceHandle, fileName: null, confidence: null);

    private void ShowConfirm(System.Drawing.Bitmap crop, IntPtr sourceHandle, string? fileName, double? confidence)
    {
        _confirmVm?.Dispose();
        _confirmVm = new PreviewConfirmViewModel(
            crop, sourceHandle, _capture, _matcher, new CaptureSaver(_sessionVm.SaveFolder));
        if (fileName is not null)
        {
            _confirmVm.FileName = fileName;
        }
        if (confidence is not null)
        {
            _confirmVm.Confidence = confidence.Value;
        }

        var view = new PreviewConfirmView();
        view.Saved += OnConfirmSaved;
        view.RetakeRequested += (_, _) =>
        {
            DisposeConfirm();
            if (_regionVm is not null)
            {
                SetContent(BuildRegionView(_regionVm)); // re-edit has no region VM -> guarded below
            }
            else
            {
                ReturnHome();
            }
        };
        view.Bind(_confirmVm);
        SetContent(view);
    }

    private void OnConfirmSaved(object? sender, string fileName)
    {
        var path = Path.Combine(_sessionVm.SaveFolder, fileName);
        var confidence = _confirmVm!.Confidence;

        if (_editingRow is not null)
        {
            _editingRow.Confidence = confidence; // re-edit overwrote the file; update the row in place
            _editingRow = null;
        }
        else
        {
            _sessionVm.Add(path, confidence, _sourceHandle);
        }

        DisposeConfirm();
        DisposeRegion();
        ShowSession();
    }

    private void StartReEdit(SessionRow row)
    {
        var crop = LoadDetached(row.FilePath);
        if (crop is null)
        {
            return; // unreadable file; stay on the session panel
        }

        _editingRow = row;
        _sourceHandle = row.SourceHandle;
        DisposeRegion(); // no region step on re-edit
        ShowConfirm(crop, row.SourceHandle, row.FileName, row.Confidence);
    }

    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose save folder",
            InitialDirectory = _sessionVm.SaveFolder,
        };
        if (dialog.ShowDialog() == true)
        {
            _sessionVm.SaveFolder = dialog.FolderName;
        }
    }

    // Load a PNG into an independent bitmap so the source file stays unlocked (re-edit Save can overwrite it).
    private static System.Drawing.Bitmap? LoadDetached(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var loaded = new System.Drawing.Bitmap(stream);
            return new System.Drawing.Bitmap(loaded);
        }
        catch
        {
            return null;
        }
    }

    private static string DefaultFolder()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BotCapture");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void DisposeRegion()
    {
        _regionVm?.Dispose();
        _regionVm = null;
    }

    private void DisposeConfirm()
    {
        _confirmVm?.Dispose();
        _confirmVm = null;
    }
}
```

- [ ] **Step 2: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add BotCapture/MainWindow.xaml.cs
git commit -m "feat(capture): session-home navigation (new capture appends row; re-test/delete/re-edit)"
```

---

## Task 5: Integrated `--output` mode + App arg wiring (WPF, visual)

Adds the integrated single-shot mode: `MainWindow` takes an optional output path; with one set, it skips the session panel, runs one picker→region→confirm flow that saves to the exact path, and exits 0 (cancel/close exits 1). `App` parses args on startup.

**Files:**
- Modify: `BotCapture/MainWindow.xaml.cs`, `BotCapture/App.xaml`, `BotCapture/App.xaml.cs`

- [ ] **Step 1: Add integrated mode to `BotCapture/MainWindow.xaml.cs`.** The `_outputPath` field already exists (added in Task 4). Make three full-method replacements; everything else (`ReturnHome`, `BuildRegionView`, `StartReEdit`, etc.) is unchanged.

(a) Replace the **constructor** (it gains the `outputPath` parameter, assigns the field, and branches the startup screen):

```csharp
    public MainWindow(string? outputPath = null)
    {
        InitializeComponent();
        _outputPath = outputPath;

        _pickerVm = new WindowPickerViewModel(new Win32WindowEnumerator(), _capture);
        _pickerView = new WindowPickerView { DataContext = _pickerVm };
        _pickerView.CaptureAccepted += OnCaptureAccepted;

        _sessionVm = new SessionViewModel(_capture, _matcher, DefaultFolder());
        _sessionView = new SessionView { DataContext = _sessionVm };
        _sessionView.NewCaptureRequested += (_, _) => StartNewCapture();
        _sessionView.RetestRequested += (_, row) => _sessionVm.Retest(row);
        _sessionView.DeleteRequested += (_, row) => _sessionVm.Remove(row);
        _sessionView.ReEditRequested += (_, row) => StartReEdit(row);
        _sessionView.BrowseFolderRequested += (_, _) => BrowseFolder();

        if (_outputPath is not null)
        {
            // Integrated: fail-by-default until a save succeeds, then jump straight into the capture flow.
            Environment.ExitCode = 1;
            _pickerVm.Refresh();
            SetContent(_pickerView);
        }
        else
        {
            ShowSession();
        }
    }
```

(b) Replace the **entire `ShowConfirm` method** (integrated save folder + exact filename):

```csharp
    private void ShowConfirm(System.Drawing.Bitmap crop, IntPtr sourceHandle, string? fileName, double? confidence)
    {
        _confirmVm?.Dispose();

        var saveFolder = _outputPath is not null ? Path.GetDirectoryName(_outputPath)! : _sessionVm.SaveFolder;
        _confirmVm = new PreviewConfirmViewModel(crop, sourceHandle, _capture, _matcher, new CaptureSaver(saveFolder));
        if (_outputPath is not null)
        {
            _confirmVm.FileName = Path.GetFileName(_outputPath); // integrated: write exactly the requested file
        }
        if (fileName is not null)
        {
            _confirmVm.FileName = fileName;
        }
        if (confidence is not null)
        {
            _confirmVm.Confidence = confidence.Value;
        }

        var view = new PreviewConfirmView();
        view.Saved += OnConfirmSaved;
        view.RetakeRequested += (_, _) =>
        {
            DisposeConfirm();
            if (_regionVm is not null)
            {
                SetContent(BuildRegionView(_regionVm));
            }
            else
            {
                ReturnHome();
            }
        };
        view.Bind(_confirmVm);
        SetContent(view);
    }
```

(c) Replace the **entire `OnConfirmSaved` method** (integrated exits; standalone appends/updates a row):

```csharp
    private void OnConfirmSaved(object? sender, string fileName)
    {
        if (_outputPath is not null)
        {
            Environment.ExitCode = 0;       // integrated single-shot: saved successfully
            Application.Current.Shutdown();
            return;
        }

        var path = Path.Combine(_sessionVm.SaveFolder, fileName);
        var confidence = _confirmVm!.Confidence;

        if (_editingRow is not null)
        {
            _editingRow.Confidence = confidence; // re-edit overwrote the file; update the row in place
            _editingRow = null;
        }
        else
        {
            _sessionVm.Add(path, confidence, _sourceHandle);
        }

        DisposeConfirm();
        DisposeRegion();
        ShowSession();
    }
```

> In integrated mode `CaptureSaver(Path.GetDirectoryName(_outputPath))` + `FileName = Path.GetFileName(_outputPath)` writes the PNG to exactly `_outputPath` and the sidecar to `_outputPath + ".meta.json"`. The process exit code comes from `Environment.ExitCode` (WPF `Main` is void): it starts at 1 in the integrated constructor and is set to 0 only on a successful save, so closing the window without saving exits 1 for BotBuilder (M8) to detect.

- [ ] **Step 2: Wire arg parsing in `App`.** Replace `BotCapture/App.xaml` (remove `StartupUri`):

```xml
<Application x:Class="BotCapture.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
```

Replace `BotCapture/App.xaml.cs`:

```csharp
using System.Windows;
using BotCapture.Core;

namespace BotCapture;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? outputPath;
        try
        {
            outputPath = CommandLineArgs.Parse(e.Args).OutputPath;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "BotCapture", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        new MainWindow(outputPath).Show();
    }
}
```

- [ ] **Step 3: Build** — `dotnet build ADB.slnx -c Debug --nologo` → 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add BotCapture/MainWindow.xaml.cs BotCapture/App.xaml BotCapture/App.xaml.cs
git commit -m "feat(capture): integrated --output single-shot mode (save to path, exit codes)"
```

---

## Task 6: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution** — `dotnet build ADB.slnx -c Debug --nologo` → `0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run the whole test suite** — `dotnet test ADB.slnx -c Debug --nologo --no-build`. New BotCapture.Core tests this slice: CommandLineArgs 4, SessionViewModel 5. BotCapture.Core.Tests should total 41 (was 32). AdbCore 226 / BotBuilder.Core 103 / BotRunner 19 unchanged.

- [ ] **Step 3: Manual run (user visual verification).**
  - Standalone: `dotnet run --project BotCapture/BotCapture.csproj -c Debug` → session panel opens. **+ New Capture** → pick a window, capture, region, confirm, **Save** → a row appears with the filename + confidence. **Re-test** shows 🟢/🔴. **🗑** removes it. **Double-click** a row → re-opens preview/confirm on that template (adjust confidence, Save overwrites). **Browse…** changes the save folder.
  - Integrated: `dotnet run --project BotCapture/BotCapture.csproj -c Debug -- --output "%USERPROFILE%\Pictures\BotCapture\itest.png"` → no session panel; capture→region→confirm→**Save** writes `itest.png` + `itest.png.meta.json` and the app exits (exit code 0). Closing without saving exits non-zero.

> Hand off to the user for visual confirmation before opening the PR.
