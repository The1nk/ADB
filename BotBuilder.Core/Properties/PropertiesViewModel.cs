using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using BotBuilder.Core.Picker;
using BotBuilder.Core.Targets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Properties;

/// <summary>State for the Properties Panel: the selected node, its action's config fields, target
/// selection, and retry visibility. Rebuilds whenever the editor's selected node changes.</summary>
public partial class PropertiesViewModel : ObservableObject
{
    private readonly BotEditorViewModel _editor;
    private readonly ActionRegistry _registry;

    [ObservableProperty] private NodeViewModel? _node;
    [ObservableProperty] private bool _supportsRetry;
    [ObservableProperty] private string _actionTitle = string.Empty;

    public PropertiesViewModel(BotEditorViewModel editor, ActionRegistry registry)
    {
        _editor = editor;
        _registry = registry;
        Fields = new ObservableCollection<ConfigFieldViewModel>();
        editor.PropertyChanged += OnEditorPropertyChanged;
        Rebuild();
    }

    public ObservableCollection<ConfigFieldViewModel> Fields { get; }

    /// <summary>The configured targets, for the target dropdown.</summary>
    public IReadOnlyList<TargetViewModel> Targets => _editor.TargetBar.Targets;

    /// <summary>Whether the selected action exposes coordinate fields the picker can fill.</summary>
    public bool SupportsCoordinatePicking => Node is not null && CoordinateFieldMap.Supports(Node.TypeKey);

    /// <summary>Whether the selected action exposes ROI region fields the region picker can fill.</summary>
    public bool SupportsRegionPicking =>
        Node is not null
        && _registry.TryGet(Node.TypeKey, out var def) && def is not null
        && def.ConfigFields.Any(f => f.Key == TemplateMatchCore.RegionWidthKey);

    /// <summary>The selected node's assigned target id (null = the default first target).</summary>
    public Guid? SelectedTargetId
    {
        get => Node?.TargetId;
        set
        {
            if (Node is not null)
            {
                _editor.AssignTarget(Node, value);
            }
        }
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BotEditorViewModel.SelectedNode))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        Node = _editor.SelectedNode;
        Fields.Clear();

        if (Node is null)
        {
            SupportsRetry = false;
            ActionTitle = string.Empty;
        }
        else
        {
            var definition = _registry.TryGet(Node.TypeKey, out var def) ? def : null;
            SupportsRetry = definition?.SupportsRetry ?? false;
            ActionTitle = definition?.DisplayName ?? Node.TypeKey;

            if (definition is not null)
            {
                foreach (var field in definition.ConfigFields)
                {
                    var node = Node;
                    Action onChanged = node.TypeKey == RunParallelAction.RunParallelTypeKey && field.Key == RunParallelAction.BranchesKey
                        ? () => _editor.OnBranchCountChanged(node)
                        : _editor.MarkDirty;
                    Fields.Add(new ConfigFieldViewModel(node, field, onChanged));
                }
            }
        }

        OnPropertyChanged(nameof(SelectedTargetId));
        OnPropertyChanged(nameof(Targets));
        OnPropertyChanged(nameof(SupportsCoordinatePicking));
        OnPropertyChanged(nameof(SupportsRegionPicking));
    }
}
