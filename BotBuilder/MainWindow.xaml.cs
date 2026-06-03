using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using BotBuilder.Core.Palette;
using BotBuilder.Core.Properties;
using Microsoft.Win32;

namespace BotBuilder;

public partial class MainWindow : Window
{
    private const string BotFilter = "Bot files (*.bot)|*.bot|All files (*.*)|*.*";

    private readonly BotEditorViewModel _editor;
    private readonly BotBuilder.Core.Integration.RunStatusTracker _runStatus = new();
    private System.Collections.Generic.Dictionary<System.Guid, BotBuilder.Core.NodeViewModel> _runNodeById = new();

    private NodeViewModel? _draggingNode;
    private Point _dragStartPointerOnCanvas;
    private double _dragStartNodeX;
    private double _dragStartNodeY;
    private Point _paletteMouseDownPoint;

    private NodeViewModel? _connectSourceNode;
    private PortViewModel? _connectSourcePort;

    private bool _isPanning;
    private Point _panLastPoint;

    private bool _isMarqueeing;
    private Point _marqueeStartWorld;

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

    private void PaletteItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _paletteMouseDownPoint = e.GetPosition(this);

        if (e.ClickCount == 2 && PaletteItemFrom(sender) is { } item)
        {
            // Drop at the center of the *visible* viewport, mapped back to world space, so the node
            // lands where the user is looking regardless of pan/zoom (ViewportHost is the on-screen
            // viewport; NodeHost lives inside the pan/zoom transform).
            var (worldX, worldY) = _editor.Viewport.ScreenToWorld(ViewportHost.ActualWidth / 2, ViewportHost.ActualHeight / 2);
            _editor.AddNode(item.TypeKey, worldX, worldY);
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

    // Clicking anywhere on the canvas moves keyboard focus off any active properties textbox. This both
    // commits a pending LostFocus-bound edit (e.g. a numeric field) BEFORE the selection changes — it
    // tunnels ahead of the node's click handler — and frees the Delete key, which a focused TextBox would
    // otherwise swallow before it reaches Window_KeyDown.
    private void ViewportHost_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ViewportHost.IsKeyboardFocusWithin)
        {
            ViewportHost.Focus();
        }
    }

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
            _editor.CommitMove(_draggingNode, _dragStartNodeX, _dragStartNodeY);
            _draggingNode = null;
        }
    }

    private void AddTarget_Click(object sender, RoutedEventArgs e) => _editor.TargetBar.AddTarget();

    private void RemoveTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BotBuilder.Core.Targets.TargetViewModel target })
        {
            _editor.TargetBar.RemoveTarget(target);
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => _editor.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _editor.Redo();
    private void Delete_Click(object sender, RoutedEventArgs e) => _editor.DeleteSelection();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F5)
        {
            TestRun_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
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

    private void InputPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Connections are dragged starting from an OUTPUT port; ignore input-port mouse-down.
    }

    private void OutputPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PortViewModel port } fe && NodeOf(fe) is { } node)
        {
            _connectSourceNode = node;
            _connectSourcePort = port;
            ((UIElement)sender).CaptureMouse();
            NodeHost.MouseLeftButtonUp += FinishConnectionDrag;
            e.Handled = true;
        }
    }

    private void FinishConnectionDrag(object sender, MouseButtonEventArgs e)
    {
        // Capture the drop position BEFORE releasing capture. (While the mouse is captured
        // to the source port, Mouse.DirectlyOver reports the captured element, not the element
        // actually under the pointer — so we resolve the drop target geometrically instead.)
        var dropPosition = e.GetPosition(NodeHost);

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

        // Dropping anywhere on a node connects the dragged output to that node's input port; the editor's
        // ConnectionValidator no-ops invalid drops (self, duplicate, occupied source port, cycle). A drop on
        // empty canvas resolves to no node and does nothing.
        var hit = System.Windows.Media.VisualTreeHelper.HitTest(NodeHost, dropPosition)?.VisualHit;
        if (hit is { } h && NodeOf(h) is { } targetNode && targetNode.InputPorts.FirstOrDefault() is { } targetPort)
        {
            _editor.Connect(source, sourcePort, targetNode, targetPort);
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

    private static PaletteItem? PaletteItemFrom(object sender)
        => (sender as FrameworkElement)?.DataContext as PaletteItem;

    private void BrowseField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ConfigFieldViewModel field })
        {
            return;
        }

        var isImage = field.Type == AdbCore.Actions.ConfigFieldType.ImagePath;
        var dialog = new OpenFileDialog
        {
            Filter = isImage
                ? "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
                : "All files (*.*)|*.*",
            CheckFileExists = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            field.Value = dialog.FileName;
        }
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        var anchor = e.GetPosition(ViewportHost);
        _editor.Viewport.ZoomAt(anchor.X, anchor.Y, factor);
        e.Handled = true;
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _isPanning = true;
        _panLastPoint = e.GetPosition(ViewportHost);
        ViewportHost.CaptureMouse();
        ViewportHost.MouseMove += Viewport_PanMouseMove;
        ViewportHost.MouseUp += Viewport_PanMouseUp;
        e.Handled = true;
    }

    private void Viewport_PanMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var p = e.GetPosition(ViewportHost);
        _editor.Viewport.Pan(p.X - _panLastPoint.X, p.Y - _panLastPoint.Y);
        _panLastPoint = p;
    }

    private void Viewport_PanMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _isPanning = false;
        ViewportHost.ReleaseMouseCapture();
        ViewportHost.MouseMove -= Viewport_PanMouseMove;
        ViewportHost.MouseUp -= Viewport_PanMouseUp;
        e.Handled = true;
    }

    private void Canvas_MarqueeStart(object sender, MouseButtonEventArgs e)
    {
        _isMarqueeing = true;
        _marqueeStartWorld = e.GetPosition(NodeHost);

        UpdateMarqueeRect(_marqueeStartWorld, _marqueeStartWorld);
        MarqueeRect.Visibility = Visibility.Visible;

        ViewportHost.CaptureMouse();
        ViewportHost.MouseMove += Canvas_MarqueeMove;
        ViewportHost.MouseLeftButtonUp += Canvas_MarqueeEnd;
        e.Handled = true;
    }

    private void Canvas_MarqueeMove(object sender, MouseEventArgs e)
    {
        if (!_isMarqueeing)
        {
            return;
        }
        UpdateMarqueeRect(_marqueeStartWorld, e.GetPosition(NodeHost));
    }

    private void Canvas_MarqueeEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_isMarqueeing)
        {
            return;
        }

        _isMarqueeing = false;
        ViewportHost.ReleaseMouseCapture();
        ViewportHost.MouseMove -= Canvas_MarqueeMove;
        ViewportHost.MouseLeftButtonUp -= Canvas_MarqueeEnd;
        MarqueeRect.Visibility = Visibility.Collapsed;

        var end = e.GetPosition(NodeHost);
        var x = Math.Min(_marqueeStartWorld.X, end.X);
        var y = Math.Min(_marqueeStartWorld.Y, end.Y);
        var w = Math.Abs(end.X - _marqueeStartWorld.X);
        var h = Math.Abs(end.Y - _marqueeStartWorld.Y);

        _editor.SelectNodes(BotBuilder.Core.Canvas.MarqueeSelection.NodesInRect(_editor.Nodes, x, y, w, h));
        e.Handled = true;
    }

    private void UpdateMarqueeRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        Canvas.SetLeft(MarqueeRect, x);
        Canvas.SetTop(MarqueeRect, y);
        MarqueeRect.Width = Math.Abs(b.X - a.X);
        MarqueeRect.Height = Math.Abs(b.Y - a.Y);
    }

    private void TestRun_Click(object sender, RoutedEventArgs e)
    {
        // 1. Serialize the current editor state to a temp .bot so the run never depends on a saved file.
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "adb-testrun");
        System.IO.Directory.CreateDirectory(dir);
        // BotName is free-form, so strip any characters that aren't valid in a filename (fall back to "bot").
        var safeName = string.Concat(_editor.BotName.Split(System.IO.Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "bot";
        }
        var botPath = System.IO.Path.Combine(dir, $"{safeName}.bot");
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
        var session = RunSession.Start(exe, args);
        BeginRunStatus(session);
        LogPanel.Visibility = Visibility.Visible;
        LogPanel.Attach(session);
    }

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
        // If no run-end line set a final status (stopped, or crashed before emitting any output), reflect
        // the exit here. When run-end already set Succeeded/Failed, leave that text in place.
        var finalized = _runStatus.Status is BotBuilder.Core.Integration.RunStatus.Succeeded
            or BotBuilder.Core.Integration.RunStatus.Failed;
        if (!finalized)
        {
            RunStatusText.Text = code == 0 ? "Run: finished" : $"Run: stopped (exit {code})";
        }
    }

    private static string? ResolveRunner()
        => BotBuilder.Core.Integration.ExeLocator.Locate(
            BotBuilder.Core.Integration.ExeLocator.Candidates(System.AppContext.BaseDirectory, "BotRunner.exe"),
            System.IO.File.Exists);
}
