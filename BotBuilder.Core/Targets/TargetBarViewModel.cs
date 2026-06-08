using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using AdbCore.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Targets;

/// <summary>The bar of bot targets. Raises <see cref="Changed"/> whenever the set of targets, or any
/// target's properties, change (so node badges can be refreshed).</summary>
public partial class TargetBarViewModel : ObservableObject
{
    public TargetBarViewModel()
    {
        Targets = new ObservableCollection<TargetViewModel>();
        Targets.CollectionChanged += OnCollectionChanged;
    }

    public ObservableCollection<TargetViewModel> Targets { get; }

    /// <summary>Raised when targets are added/removed or a target property changes.</summary>
    public event EventHandler? Changed;

    public TargetViewModel AddTarget()
    {
        var target = new TargetViewModel
        {
            Id = Guid.NewGuid(),
            Name = $"Target {Targets.Count + 1}",
            Type = BotTargetType.Window,
            Selector = string.Empty,
        };
        Targets.Add(target);
        return target;
    }

    public void RemoveTarget(TargetViewModel target) => Targets.Remove(target);

    /// <summary>Resolves the target a node is bound to: its explicit <paramref name="targetId"/> when set
    /// (returning null when set but no longer present), otherwise the first target — or null when there are
    /// no targets at all.</summary>
    public TargetViewModel? ResolveForNode(Guid? targetId)
        => targetId is Guid id
            ? Targets.FirstOrDefault(t => t.Id == id)
            : Targets.FirstOrDefault();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TargetViewModel t in e.OldItems) { t.PropertyChanged -= OnTargetPropertyChanged; }
        }
        if (e.NewItems is not null)
        {
            foreach (TargetViewModel t in e.NewItems) { t.PropertyChanged += OnTargetPropertyChanged; }
        }
        RaiseChanged();
    }

    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseChanged();

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
