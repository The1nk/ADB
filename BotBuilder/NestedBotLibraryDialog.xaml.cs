using System.Linq;
using System.Windows;
using BotBuilder.Core.NestedBots;

namespace BotBuilder;

public partial class NestedBotLibraryDialog : Window
{
    private readonly NestedBotLibraryManagerViewModel _vm;

    public NestedBotLibraryDialog(NestedBotLibraryManagerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not NestedBotEntryRow row) { return; }
        if (row.UsageCount > 0)
        {
            var confirm = MessageBox.Show(this,
                $"'{row.Name}' is still referenced by {row.UsageCount} card(s). Remove it anyway? Those cards will show '(missing bot)'.",
                "Remove nested bot", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) { return; }
        }
        _vm.Remove(row.Id);
    }

    private void RemoveUnused_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this,
            "Remove every nested bot not reachable from the top-level graph? This cannot be undone (but isn't saved until you save the file).",
            "Remove unused nested bots", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) { return; }
        var removed = _vm.RemoveUnused();
        MessageBox.Show(this, removed == 0 ? "No unused nested bots to remove." : $"Removed {removed} unused nested bot(s).",
            "Remove unused nested bots", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
