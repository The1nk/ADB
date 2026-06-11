using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.NestedBots;
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
    [ObservableProperty] private string? _cycleWarning;

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

    /// <summary>Whether the selected node is a Nested Bot card (drives the panel's nested-bot section).</summary>
    public bool IsNestedBotCard => Node is not null && Node.TypeKey == NestedBotAction.NestedBotTypeKey;

    /// <summary>The library entries for the picker dropdown. A fresh list each get so a rename re-renders.</summary>
    public IReadOnlyList<Bot> NestedBotEntries => _editor.NestedBotLibrary.Entries.ToList();

    /// <summary>The selected card's referenced library bot id (null = unassigned).</summary>
    public Guid? SelectedNestedBotId
    {
        get => Node is not null
            && Node.Config.TryGetValue(NestedBotAction.NestedBotIdKey, out var raw)
            && Guid.TryParse(raw?.ToString(), out var id) ? id : null;
        set
        {
            if (Node is null) { return; }
            if (value is Guid id)
            {
                if (_editor.NestedBotLibrary.WouldCreateCycle(_editor.BotId, id))
                {
                    CycleWarning = "That would make this bot run itself (a nested-bot cycle).";
                    OnPropertyChanged(nameof(SelectedNestedBotId)); // snap the picker back
                    return;
                }
                Node.Config[NestedBotAction.NestedBotIdKey] = id.ToString();
            }
            else
            {
                Node.Config.Remove(NestedBotAction.NestedBotIdKey);
            }
            CycleWarning = null;
            _editor.MarkDirty();
            _editor.RefreshNestedBotSubtitles();
            OnPropertyChanged(nameof(SelectedNestedBotName));
            OnPropertyChanged(nameof(SelectedNestedBotEditableName));
        }
    }

    /// <summary>The referenced bot's name (or a placeholder) — read-only display.</summary>
    public string SelectedNestedBotName =>
        Node is null ? string.Empty : NestedBotCardInfo.Resolve(Node.Config, _editor.NestedBotLibrary);

    /// <summary>Two-way name of the selected entry: setting it renames the library entry live (and every card
    /// that references it). Empty/whitespace is ignored.</summary>
    public string SelectedNestedBotEditableName
    {
        get => SelectedNestedBotId is Guid id ? (_editor.NestedBotLibrary.Get(id)?.Name ?? string.Empty) : string.Empty;
        set
        {
            if (SelectedNestedBotId is Guid id && !string.IsNullOrWhiteSpace(value))
            {
                _editor.NestedBotLibrary.Rename(id, value);
                _editor.RefreshNestedBotSubtitles();
                OnPropertyChanged(nameof(SelectedNestedBotName));
                OnPropertyChanged(nameof(NestedBotEntries));
            }
        }
    }

    /// <summary>Imports an external bot as a new library entry and assigns it to the selected card.</summary>
    public Bot ImportNestedBot(Bot external)
    {
        var entry = _editor.NestedBotLibrary.Import(external);
        SelectedNestedBotId = entry.Id; // assigns + refreshes subtitle
        OnPropertyChanged(nameof(NestedBotEntries));
        OnPropertyChanged(nameof(SelectedNestedBotEditableName));
        return entry;
    }

    /// <summary>Creates a new empty library entry and assigns it to the selected card. Returns the entry so the
    /// caller can open a child editor for it.</summary>
    public AdbCore.Models.Bot NewNestedBot()
    {
        var entry = _editor.NestedBotLibrary.AddNew();
        SelectedNestedBotId = entry.Id;
        OnPropertyChanged(nameof(NestedBotEntries));
        return entry;
    }

    /// <summary>Removes the selected entry from the library and unassigns the card.</summary>
    public void RemoveSelectedNestedBot()
    {
        if (SelectedNestedBotId is Guid id)
        {
            _editor.NestedBotLibrary.Remove(id);
            SelectedNestedBotId = null;
            OnPropertyChanged(nameof(NestedBotEntries));
        }
    }

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
        CycleWarning = null;
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
        OnPropertyChanged(nameof(IsNestedBotCard));
        OnPropertyChanged(nameof(NestedBotEntries));
        OnPropertyChanged(nameof(SelectedNestedBotId));
        OnPropertyChanged(nameof(SelectedNestedBotName));
        OnPropertyChanged(nameof(SelectedNestedBotEditableName));
    }
}
