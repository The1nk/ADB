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

    // Gesture handlers are wired in the next task. Stubs keep the XAML compiling.
    private void New_Click(object sender, RoutedEventArgs e) { }
    private void Open_Click(object sender, RoutedEventArgs e) { }
    private void Save_Click(object sender, RoutedEventArgs e) { }
    private void PaletteItem_MouseMove(object sender, MouseEventArgs e) { }
    private void PaletteItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }
    private void Canvas_Drop(object sender, DragEventArgs e) { }
    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }
}
