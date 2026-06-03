using System.Windows;
using AdbCore.Screen;
using AdbCore.Targets;
using BotCapture.Core;

namespace BotCapture;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new WindowPickerViewModel(new Win32WindowEnumerator(), new Win32WindowCapture());
        Picker.DataContext = viewModel;
        viewModel.Refresh();
    }
}
