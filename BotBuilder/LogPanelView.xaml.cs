using System;
using System.Globalization;
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
        // Stamp the receive time in the system locale's long-time format (includes seconds, like a log wants).
        var timestamp = DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
        var item = new ListBoxItem
        {
            Content = $"[{timestamp}] {entry.Display}",
            Foreground = entry.Kind == RunLogKind.Action && entry.Success == false ? Brushes.DarkRed
                       : entry.Kind == RunLogKind.RunEnd && entry.Success == false ? Brushes.DarkRed
                       : Brushes.Black,
        };
        LogList.Items.Add(item);
        LogList.ScrollIntoView(item);
    }

    private void OnStop(object sender, RoutedEventArgs e) => _session?.Stop();

    private void OnClear(object sender, RoutedEventArgs e) => LogList.Items.Clear();

    /// <summary>Collapses the panel to reclaim canvas space. The running session (if any) keeps going;
    /// the next test run re-shows the panel via its visibility binding in MainWindow.</summary>
    private void OnHide(object sender, RoutedEventArgs e) => Visibility = Visibility.Collapsed;
}
