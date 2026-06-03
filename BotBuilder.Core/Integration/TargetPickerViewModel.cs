using System.Collections.ObjectModel;
using System.ComponentModel;
using AdbCore.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Integration;

/// <summary>Drives the target-picker dialog: one editable <see cref="TargetSelectionRow"/> per declared
/// target, and a live <see cref="CommandPreview"/> of the equivalent BotRunner command.</summary>
public partial class TargetPickerViewModel : ObservableObject
{
    private readonly string _exeName;
    private readonly string _botPath;

    public TargetPickerViewModel(
        string exeName, string botPath, IEnumerable<(string Name, BotTargetType Type, string Selector)> targets)
    {
        _exeName = exeName;
        _botPath = botPath;

        Rows = new ObservableCollection<TargetSelectionRow>();
        foreach (var (name, type, selector) in targets)
        {
            var row = new TargetSelectionRow(name, type) { Selector = selector };
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }
    }

    public ObservableCollection<TargetSelectionRow> Rows { get; }

    /// <summary>The copy-pasteable equivalent command, recomputed as selectors change.</summary>
    public string CommandPreview =>
        RunCommandBuilder.BuildDisplayCommand(_exeName, _botPath, PairList());

    /// <summary>The (name, selector) pairs to hand to <see cref="RunCommandBuilder.BuildArgs"/>.</summary>
    public IReadOnlyList<(string Name, string Selector)> Selectors() => PairList();

    private List<(string Name, string Selector)> PairList()
        => Rows.Select(r => (r.Name, r.Selector)).ToList();

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetSelectionRow.Selector))
        {
            OnPropertyChanged(nameof(CommandPreview));
        }
    }
}
