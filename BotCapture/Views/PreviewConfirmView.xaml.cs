using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BotCapture.Core;

namespace BotCapture.Views;

public partial class PreviewConfirmView : UserControl
{
    public PreviewConfirmView()
    {
        InitializeComponent();
    }

    /// <summary>Raised after a successful Save, with the saved file name.</summary>
    public event EventHandler<string>? Saved;

    /// <summary>Raised when the user wants to re-select a region.</summary>
    public event EventHandler? RetakeRequested;

    private PreviewConfirmViewModel? Vm => DataContext as PreviewConfirmViewModel;

    /// <summary>Call after constructing the VM to bind it and show the crop previews.</summary>
    public void Bind(PreviewConfirmViewModel vm)
    {
        DataContext = vm;
        var image = BitmapInterop.ToImageSource(vm.Crop);
        Preview1x.Source = image;
        Preview1x.Width = vm.Crop.Width;
        Preview1x.Height = vm.Crop.Height;
        Preview2x.Source = image;
        Preview2x.Width = vm.Crop.Width * 2;
        Preview2x.Height = vm.Crop.Height * 2;
        MatchStatus.Text = string.Empty;
        SaveStatus.Text = string.Empty;
    }

    private void OnTestMatch(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.TestMatch();
        var o = Vm.LastOutcome;
        if (o is null) return;

        if (o.Error is not null)
        {
            MatchStatus.Foreground = Brushes.DarkRed;
            MatchStatus.Text = $"Test Match failed: {o.Error}";
        }
        else if (o.Matched && o.Location is { } loc)
        {
            MatchStatus.Foreground = Brushes.Green;
            MatchStatus.Text = $"✅ Match — {o.Score:F2} @ ({loc.X},{loc.Y})";
        }
        else
        {
            MatchStatus.Foreground = Brushes.DarkRed;
            MatchStatus.Text = $"🔴 No match — best {o.Score:F2}, threshold {Vm.Confidence:F2}";
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.Save();
        SaveStatus.Text = $"Saved {Vm.FileName}";
        Saved?.Invoke(this, Vm.FileName);
    }

    private void OnRetake(object sender, RoutedEventArgs e) => RetakeRequested?.Invoke(this, EventArgs.Empty);
}
