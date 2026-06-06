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
            MatchStatus.Foreground = TryFindResource("ErrorBrush") as Brush ?? Brushes.DarkRed;
            MatchStatus.Text = $"Test Match failed: {o.Error}";
        }
        else if (o.Matched && o.Location is { } loc)
        {
            MatchStatus.Foreground = TryFindResource("SuccessBrush") as Brush ?? Brushes.Green;
            MatchStatus.Text = $"✅ Match — {FormatScore(o.Score.GetValueOrDefault())} @ ({loc.X},{loc.Y})";
        }
        else
        {
            MatchStatus.Foreground = TryFindResource("ErrorBrush") as Brush ?? Brushes.DarkRed;
            MatchStatus.Text =
                $"🔴 No match — best {FormatScore(o.Score.GetValueOrDefault())}, threshold {Vm.Confidence:F3}";
        }
    }

    // Template-match score (CCOEFF_NORMED) is rarely exactly 1.0, so a near-perfect match lands just under
    // the threshold. Truncate toward zero to 3 decimals so a sub-threshold score is shown honestly (e.g.
    // 0.9985 -> "0.998") instead of being rounded UP to look like it meets the threshold (e.g. "1.00").
    private static string FormatScore(double score) =>
        (Math.Truncate(score * 1000) / 1000).ToString("0.000");

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.Save();
        SaveStatus.Text = $"Saved {Vm.FileName}";
        Saved?.Invoke(this, Vm.FileName);
    }

    private void OnRetake(object sender, RoutedEventArgs e) => RetakeRequested?.Invoke(this, EventArgs.Empty);
}
