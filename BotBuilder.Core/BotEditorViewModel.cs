using System.Collections.ObjectModel;
using AdbCore.Actions;
using AdbCore.Serialization;
using BotBuilder.Core.Canvas;
using BotBuilder.Core.Connections;
using BotBuilder.Core.Palette;
using BotBuilder.Core.Properties;
using BotBuilder.Core.Targets;
using BotBuilder.Core.Undo;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core;

/// <summary>Root view-model for the editor: nodes, connections, selection, undoable operations.</summary>
public partial class BotEditorViewModel : ObservableObject
{
    private readonly ActionRegistry _registry;
    private readonly BotSerializer _serializer = new();
    private readonly UndoStack _undo = new();

    [ObservableProperty] private string _botName = "Untitled";
    [ObservableProperty] private NodeViewModel? _selectedNode;
    [ObservableProperty] private ConnectionViewModel? _selectedConnection;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _filePath;

    public BotEditorViewModel(ActionRegistry registry)
    {
        _registry = registry;
        Palette = new PaletteViewModel(registry);
        Nodes = new ObservableCollection<NodeViewModel>();
        Connections = new ObservableCollection<ConnectionViewModel>();
        Viewport = new CanvasViewport();
        TargetBar = new TargetBarViewModel();
        TargetBar.Changed += OnTargetsChanged;
        Properties = new PropertiesViewModel(this, registry);
        New();
    }

    public ObservableCollection<NodeViewModel> Nodes { get; }
    public ObservableCollection<ConnectionViewModel> Connections { get; }
    public PaletteViewModel Palette { get; }
    public CanvasViewport Viewport { get; }
    public TargetBarViewModel TargetBar { get; }
    public PropertiesViewModel Properties { get; }
    public Guid BotId { get; private set; }

    /// <summary>Marks the document dirty (used by property edits that don't go through the undo stack).</summary>
    public void MarkDirty() => IsDirty = true;

    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;

    public NodeViewModel AddNode(string typeKey, double x, double y)
    {
        var definition = _registry.Get(typeKey);
        var node = NodeViewModel.FromDefinition(definition, Guid.NewGuid(), definition.DisplayName, x, y);
        _undo.Execute(new AddNodeCommand(this, node));
        AfterEdit();
        return node;
    }

    public void MoveNode(NodeViewModel node, double x, double y)
    {
        node.X = x;
        node.Y = y;
        IsDirty = true;
    }

    public void CommitMove(NodeViewModel node, double oldX, double oldY)
    {
        if (oldX == node.X && oldY == node.Y)
        {
            return;
        }
        _undo.PushExecuted(new MoveNodeCommand(node, oldX, oldY, node.X, node.Y));
        AfterEdit();
    }

    public ConnectionError Connect(NodeViewModel source, PortViewModel sourcePort, NodeViewModel target, PortViewModel targetPort)
    {
        var error = ConnectionValidator.Validate(Connections, source, sourcePort, target, targetPort);
        if (error != ConnectionError.None)
        {
            return error;
        }
        var connection = new ConnectionViewModel(Guid.NewGuid(), source, sourcePort, target, targetPort);
        _undo.Execute(new ConnectCommand(this, connection));
        AfterEdit();
        return ConnectionError.None;
    }

    public void Disconnect(ConnectionViewModel connection)
    {
        _undo.Execute(new DisconnectCommand(this, connection));
        AfterEdit();
    }

    public void DeleteNode(NodeViewModel node)
    {
        var incident = IncidentConnections(new[] { node });
        _undo.Execute(new DeleteNodesCommand(this, new[] { node }, incident));
        AfterEdit();
    }

    public void DeleteSelection()
    {
        if (SelectedConnection is { } connection)
        {
            Disconnect(connection);
            SelectedConnection = null;
            return;
        }

        var nodes = Nodes.Where(n => n.IsSelected).ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        _undo.Execute(new DeleteNodesCommand(this, nodes, IncidentConnections(nodes)));
        SelectedNode = null;
        AfterEdit();
    }

    public void Select(NodeViewModel? node)
    {
        foreach (var n in Nodes) { n.IsSelected = ReferenceEquals(n, node); }
        ClearConnectionSelection();
        SelectedConnection = null;
        SelectedNode = node;
    }

    public void SelectNodes(IEnumerable<NodeViewModel> nodes)
    {
        var selected = nodes.ToList();
        var set = new HashSet<NodeViewModel>(selected);

        foreach (var n in Nodes) { n.IsSelected = set.Contains(n); }
        foreach (var c in Connections) { c.IsSelected = false; }

        SelectedConnection = null;
        SelectedNode = selected.Count == 1 ? selected[0] : null;
    }

    public void SelectConnection(ConnectionViewModel? connection)
    {
        foreach (var c in Connections) { c.IsSelected = ReferenceEquals(c, connection); }
        foreach (var n in Nodes) { n.IsSelected = false; }
        SelectedNode = null;
        SelectedConnection = connection;
    }

    public void Undo()
    {
        if (!_undo.CanUndo)
        {
            return;
        }
        _undo.Undo();
        AfterEdit();
    }

    public void Redo()
    {
        if (!_undo.CanRedo)
        {
            return;
        }
        _undo.Redo();
        AfterEdit();
    }

    public void New()
    {
        BotId = Guid.NewGuid();
        BotName = "Untitled";
        DetachAllConnections();
        Connections.Clear();
        Nodes.Clear();
        TargetBar.Targets.Clear();
        SelectedNode = null;
        SelectedConnection = null;
        _undo.Clear();
        FilePath = null;
        IsDirty = false;
        RaiseUndoState();
    }

    public void Open(string path)
    {
        var bot = _serializer.Load(path);
        DocumentMapper.Populate(this, bot, _registry);
        _undo.Clear();
        FilePath = path;
        IsDirty = false;
        RaiseUndoState();
    }

    public void Save(string path)
    {
        _serializer.Save(DocumentMapper.ToBot(this), path);
        FilePath = path;
        IsDirty = false;
    }

    // ---- internal mutation helpers used by commands and the mapper ----

    private List<ConnectionViewModel> IncidentConnections(IReadOnlyCollection<NodeViewModel> nodes)
        => Connections
            .Where(c => nodes.Any(n => ReferenceEquals(c.Source, n) || ReferenceEquals(c.Target, n)))
            .Distinct()
            .ToList();

    internal void AddNodeCore(NodeViewModel node) => Nodes.Add(node);
    internal void RemoveNodeCore(NodeViewModel node) => Nodes.Remove(node);
    internal void AddConnectionCore(ConnectionViewModel connection)
    {
        connection.Attach();
        Connections.Add(connection);
    }

    internal void RemoveConnectionCore(ConnectionViewModel connection)
    {
        connection.Detach();
        Connections.Remove(connection);
    }

    /// <summary>Replaces editor contents during a load (mapper-only).</summary>
    internal void LoadFrom(
        Guid botId,
        string botName,
        IEnumerable<NodeViewModel> nodes,
        Func<IReadOnlyList<NodeViewModel>, IEnumerable<ConnectionViewModel>> connectionFactory)
    {
        BotId = botId;
        BotName = botName;
        DetachAllConnections();
        Connections.Clear();
        Nodes.Clear();
        foreach (var node in nodes) { Nodes.Add(node); }
        foreach (var connection in connectionFactory(Nodes)) { Connections.Add(connection); }
        SelectedNode = null;
        SelectedConnection = null;
    }

    /// <summary>Assigns a node to a target (null = the default first target) and refreshes badges.</summary>
    public void AssignTarget(NodeViewModel node, Guid? targetId)
    {
        node.TargetId = targetId;
        RefreshTargetBadges();
        IsDirty = true;
    }

    /// <summary>Recomputes every node's target badge: shown (the resolved target's name) only when the
    /// bot has more than one target; an unassigned or dangling node resolves to the first target.</summary>
    public void RefreshTargetBadges()
    {
        var targets = TargetBar.Targets;
        if (targets.Count <= 1)
        {
            foreach (var node in Nodes) { node.TargetBadge = null; }
            return;
        }

        foreach (var node in Nodes)
        {
            var resolved = targets.FirstOrDefault(t => t.Id == node.TargetId) ?? targets[0];
            node.TargetBadge = resolved.Name;
        }
    }

    private void OnTargetsChanged(object? sender, EventArgs e)
    {
        RefreshTargetBadges();
        IsDirty = true;
    }

    private void AfterEdit()
    {
        IsDirty = true;
        RaiseUndoState();
        RefreshTargetBadges();
    }

    private void RaiseUndoState()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void ClearConnectionSelection()
    {
        foreach (var c in Connections) { c.IsSelected = false; }
    }

    private void DetachAllConnections()
    {
        foreach (var c in Connections) { c.Detach(); }
    }
}
