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
