using AdbCore.Actions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Palette;

/// <summary>Searchable, category-grouped view of the registered action definitions.</summary>
public partial class PaletteViewModel : ObservableObject
{
    private readonly ActionRegistry _registry;
    private readonly IDependencyProbe _probe;

    [ObservableProperty] private string _searchText = string.Empty;

    public PaletteViewModel(ActionRegistry registry, IDependencyProbe? probe = null)
    {
        _registry = registry;
        _probe = probe ?? new DependencyProbe();
        Categories = new System.Collections.ObjectModel.ObservableCollection<PaletteCategory>();
        Rebuild();
    }

    public System.Collections.ObjectModel.ObservableCollection<PaletteCategory> Categories { get; }

    partial void OnSearchTextChanged(string value) => Rebuild();

    private void Rebuild()
    {
        Categories.Clear();

        var matches = _registry.All
            .Where(d => string.IsNullOrWhiteSpace(SearchText)
                        || d.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                        || d.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var group in matches.GroupBy(d => d.Category).OrderBy(g => g.Key))
        {
            var status = _probe.ForCategory(group.Key);
            var items = group
                .OrderBy(d => d.DisplayName)
                .Select(d => new PaletteItem(d.TypeKey, d.DisplayName, d.Category, status.IsAvailable, status.Reason))
                .ToList();
            Categories.Add(new PaletteCategory(group.Key, items, status.IsAvailable, status.Reason));
        }
    }
}
