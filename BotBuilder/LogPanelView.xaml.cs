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

    /// <summary>Attaches a freshly started session and clears the previous run's output. Any prior session
    /// is stopped and disposed first.</summary>
    public void Attach(RunSession session)
    {
        _session?.Stop();
        _session?.Dispose();
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
