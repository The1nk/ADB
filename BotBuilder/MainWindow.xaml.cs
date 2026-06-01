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

    private void Connection_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
    private void InputPort_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
    private void OutputPort_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }

    private static PaletteItem? PaletteItemFrom(object sender)
        => (sender as FrameworkElement)?.DataContext as PaletteItem;
}
