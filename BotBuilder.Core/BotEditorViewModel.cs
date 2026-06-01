using System.Collections.ObjectModel;
using AdbCore.Actions;
using AdbCore.Serialization;
using BotBuilder.Core.Palette;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core;

/// <summary>Root view-model for the editor: nodes, selection, and document operations.</summary>
public partial class BotEditorViewModel : ObservableObject
{
    private readonly ActionRegistry _registry;
    private readonly BotSerializer _serializer = new();

    [ObservableProperty] private string _botName = "Untitled";
    [ObservableProperty] private NodeViewModel? _selectedNode;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _filePath;

    public BotEditorViewModel(ActionRegistry registry)
    {
        _registry = registry;
        Palette = new PaletteViewModel(registry);
        Nodes = new ObservableCollection<NodeViewModel>();
        New();
    }

    public ObservableCollection<NodeViewModel> Nodes { get; }
    public PaletteViewModel Palette { get; }
    public Guid BotId { get; private set; }

    public NodeViewModel AddNode(string typeKey, double x, double y)
    {
        var definition = _registry.Get(typeKey);
        var node = NodeViewModel.FromDefinition(definition, Guid.NewGuid(), definition.DisplayName, x, y);
        Nodes.Add(node);
        IsDirty = true;
        return node;
    }

    public void MoveNode(NodeViewModel node, double x, double y)
    {
        node.X = x;
        node.Y = y;
        IsDirty = true;
    }

    public void Select(NodeViewModel? node)
    {
        foreach (var n in Nodes)
        {
            n.IsSelected = ReferenceEquals(n, node);
        }
        SelectedNode = node;
    }

    public void New()
    {
        BotId = Guid.NewGuid();
        BotName = "Untitled";
        Nodes.Clear();
        SelectedNode = null;
        FilePath = null;
        IsDirty = false;
    }

    public void Open(string path)
    {
        var bot = _serializer.Load(path);
        DocumentMapper.Populate(this, bot, _registry);
        FilePath = path;
        IsDirty = false;
    }

    public void Save(string path)
    {
        _serializer.Save(DocumentMapper.ToBot(this), path);
        FilePath = path;
        IsDirty = false;
    }

    /// <summary>Used by <see cref="DocumentMapper"/> to replace editor contents during a load.</summary>
    internal void LoadFrom(Guid botId, string botName, IEnumerable<NodeViewModel> nodes)
    {
        BotId = botId;
        BotName = botName;
        Nodes.Clear();
        foreach (var node in nodes)
        {
            Nodes.Add(node);
        }
        SelectedNode = null;
    }
}
