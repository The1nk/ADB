using System.Windows;
using BotBuilder.Core;

namespace BotBuilder;

public partial class PropertiesDialog : Window
{
    public PropertiesDialog(BotPropertiesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
