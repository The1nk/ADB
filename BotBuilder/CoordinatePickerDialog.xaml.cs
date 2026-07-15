using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Media;
using AdbUi.Theme.Controls;
using BotBuilder.Core.Picker;

namespace BotBuilder;

public partial class CoordinatePickerDialog : Window
{
    private readonly CoordinatePickerViewModel _vm;

    public CoordinatePickerDialog(CoordinatePickerViewModel vm, Bitmap frame)
    {
        InitializeComponent();
        _vm = vm;
        Viewer.SetImage(FrameImageSource.ToImageSource(frame));
        Viewer.ImagePointerDown += OnPointerDown;
        PromptText.Text = _vm.CurrentPrompt;
    }

    /// <summary>The collected (XKey, YKey, X, Y) write-back tuples — valid after the dialog returns true.</summary>
    public IReadOnlyList<(string XKey, string YKey, int X, int Y)> Results => _vm.Results();

    private void OnPointerDown(object? sender, ImagePointerEventArgs e)
    {
        if (!e.InsideImage)
        {
            return; // clicked outside the image (centered margin) — ignore
        }

        _vm.RecordClick(e.SourceX, e.SourceY);
        Viewer.AddDot(e.SourceX, e.SourceY, Colors.Lime, System.Windows.Media.Color.FromArgb(80, 0, 255, 0));
        PromptText.Text = _vm.CurrentPrompt;

        if (_vm.IsComplete)
        {
            DialogResult = true;
            Close();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
