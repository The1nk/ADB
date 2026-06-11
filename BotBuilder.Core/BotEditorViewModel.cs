using System.Collections.ObjectModel;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using AdbCore.Serialization;
using BotBuilder.Core.Canvas;
using BotBuilder.Core.Connections;
using BotBuilder.Core.NestedBots;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _botName = "Untitled Bot";
    [ObservableProperty] private NodeViewModel? _selectedNode;
    [ObservableProperty] private ConnectionViewModel? _selectedConnection;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _isDirty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _filePath;

    /// <summary>The main-window title: "ADB Bot Builder: [*]Name[.bot]". The dirty marker is shown for
    /// unsaved changes; the ".bot" suffix only once the document has a file (so a fresh doc reads
    /// "Untitled Bot", a saved one "Name.bot").</summary>
    public string WindowTitle =>
        $"ADB Bot Builder: {(IsDirty ? "*" : "")}{BotName}{(FilePath != null ? ".bot" : "")}";

    public BotEditorViewModel(ActionRegistry registry, NestedBotLibrary? nestedBotLibrary = null)
    {
        _registry = registry;
        NestedBotLibrary = nestedBotLibrary ?? new NestedBotLibrary();
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

    /// <summary>The root nested-bot library (shared with any child editors editing entries of it).</summary>
    public NestedBotLibrary NestedBotLibrary { get; }

    /// <summary>Marks the document dirty (used by property edits that don't go through the undo stack).</summary>
    public void MarkDirty() => IsDirty = true;

    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;

    public NodeViewModel AddNode(string typeKey, double x, double y)
    {
        var definition = _registry.Get(typeKey);
        var node = NodeViewModel.FromDefinition(definition, Guid.NewGuid(), definition.DisplayName, x, y);
        node.TargetId = AutoTargetFor(definition.Category);
        _undo.Execute(new AddNodeCommand(this, node));
        AfterEdit();
        return node;
    }

    /// <summary>The lone target whose type matches the node's category, or null when the category is
    /// target-agnostic or there isn't exactly one matching-type target.</summary>
    private Guid? AutoTargetFor(string category)
    {
        if (NodeTargetType.For(category) is not BotTargetType type) return null;
        Guid? found = null;
        var count = 0;
        foreach (var t in TargetBar.Targets)
        {
            if (t.Type == type) { found = t.Id; count++; }
        }
        return count == 1 ? found : null;
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

    /// <summary>Records a multi-node drag (each node already at its new position) as one undoable step.
    /// Nodes that didn't actually move are ignored; a no-op overall does nothing.</summary>
    public void CommitMoves(IReadOnlyList<(NodeViewModel Node, double OldX, double OldY)> moves)
    {
        var actual = moves
            .Where(m => m.OldX != m.Node.X || m.OldY != m.Node.Y)
            .Select(m => (m.Node, m.OldX, m.OldY, m.Node.X, m.Node.Y))
            .ToList();
        if (actual.Count == 0)
        {
            return;
        }
        _undo.PushExecuted(new MoveNodesCommand(actual));
        AfterEdit();
    }

    /// <summary>Frames the viewport so every node is visible — the "I panned away and lost my graph" rescue.
    /// No-op when there are no nodes. Node width is the canonical <see cref="NodeLayout.CardWidth"/>; height is
    /// each node's own.</summary>
    public void FitViewportToNodes(double viewportWidth, double viewportHeight)
    {
        if (Nodes.Count == 0)
        {
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var n in Nodes)
        {
            minX = Math.Min(minX, n.X);
            minY = Math.Min(minY, n.Y);
            maxX = Math.Max(maxX, n.X + NodeLayout.CardWidth);
            maxY = Math.Max(maxY, n.Y + n.Height);
        }

        Viewport.FitTo(minX, minY, maxX, maxY, viewportWidth, viewportHeight);
    }

    /// <summary>Re-arranges all nodes into a tidy left-to-right layered layout, as one undoable step.</summary>
    public void AutoLayout()
    {
        if (Nodes.Count == 0) return;
        var nodes = Nodes.Select(n => (n.Id, n.Height)).ToList();
        var edges = Connections.Select(c => (c.Source.Id, c.Target.Id)).ToList();
        var positions = BotBuilder.Core.Layout.AutoLayout.Arrange(nodes, edges);

        var moves = new List<(NodeViewModel Node, double OldX, double OldY)>();
        foreach (var node in Nodes)
        {
            if (positions.TryGetValue(node.Id, out var p))
            {
                var oldX = node.X; var oldY = node.Y;
                node.X = p.X; node.Y = p.Y;
                moves.Add((node, oldX, oldY));
            }
        }
        CommitMoves(moves);   // records a single MoveNodesCommand (no-op-safe)
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

    /// <summary>Clears every node's Test Run highlight (called when a run starts).</summary>
    public void ResetRunStates()
    {
        foreach (var node in Nodes)
        {
            node.RunState = NodeRunState.None;
        }
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

    private NodeClipboard? _clipboard;

    /// <summary>Snapshots the selected nodes (or the single SelectedNode) and the connections among them
    /// into the in-app clipboard. No-op when nothing is selected.</summary>
    public void CopySelection()
    {
        var selected = Nodes.Where(n => n.IsSelected).ToList();
        if (selected.Count == 0 && SelectedNode is not null) selected.Add(SelectedNode);
        if (selected.Count == 0) return;

        var indexOf = new Dictionary<NodeViewModel, int>();
        for (var i = 0; i < selected.Count; i++) indexOf[selected[i]] = i;

        var nodeClips = selected
            .Select(n => new NodeClip(n.TypeKey, n.Label, n.TargetId, n.RetryMaxAttempts, n.RetryDelayMs,
                new Dictionary<string, object>(n.Config), n.X, n.Y))
            .ToList();

        var connClips = Connections
            .Where(c => indexOf.ContainsKey(c.Source) && indexOf.ContainsKey(c.Target))
            .Select(c => new ConnectionClip(indexOf[c.Source], c.SourcePort.Name, indexOf[c.Target], c.TargetPort.Name))
            .ToList();

        _clipboard = new NodeClipboard(nodeClips, connClips);
    }

    /// <summary>Pastes the clipboard: fresh nodes (new Ids, offset +24,+24), the internal connections among
    /// them re-created by port name, added as one undoable step and selected. No-op when the clipboard is empty.</summary>
    public void Paste()
    {
        if (_clipboard is null || _clipboard.Nodes.Count == 0) return;
        const double dx = 24, dy = 24;

        var newNodes = new List<NodeViewModel>(_clipboard.Nodes.Count);
        foreach (var clip in _clipboard.Nodes)
        {
            var definition = _registry.Get(clip.TypeKey);
            var node = NodeViewModel.FromDefinition(definition, Guid.NewGuid(), clip.Label, clip.X + dx, clip.Y + dy);
            node.TargetId = clip.TargetId;
            node.RetryMaxAttempts = clip.RetryMaxAttempts;
            node.RetryDelayMs = clip.RetryDelayMs;
            node.Config.Clear();
            foreach (var kv in clip.Config) { node.Config[kv.Key] = kv.Value; }
            if (node.TypeKey == RunParallelAction.RunParallelTypeKey)
            {
                node.SetBranchPortCount(Math.Max(2, ConfigValues.GetInt(node.Config, RunParallelAction.BranchesKey, RunParallelAction.DefaultBranchCount)));
            }
            newNodes.Add(node);
        }

        var newConnections = new List<ConnectionViewModel>(_clipboard.Connections.Count);
        foreach (var cc in _clipboard.Connections)
        {
            var source = newNodes[cc.SourceIndex];
            var target = newNodes[cc.TargetIndex];
            var sp = source.OutputPorts.FirstOrDefault(p => p.Name == cc.SourcePort);
            var tp = target.InputPorts.FirstOrDefault(p => p.Name == cc.TargetPort);
            if (sp is not null && tp is not null)
            {
                newConnections.Add(new ConnectionViewModel(Guid.NewGuid(), source, sp, target, tp));
            }
        }

        _undo.Execute(new PasteCommand(this, newNodes, newConnections));
        SelectNodes(newNodes);
        AfterEdit();
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
        BotName = "Untitled Bot";
        DetachAllConnections();
        Connections.Clear();
        Nodes.Clear();
        TargetBar.Targets.Clear();
        NestedBotLibrary.Load(Array.Empty<Bot>());
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

    /// <summary>Reconciles a Run Parallel node's branch ports to its current `branches` config value
    /// (clamped to >= 2): grows/shrinks the output ports and, on a shrink, deletes the connections on the
    /// dropped ports. The whole change is a single undoable step.</summary>
    public void OnBranchCountChanged(NodeViewModel node)
    {
        var requested = ConfigValues.GetInt(node.Config, RunParallelAction.BranchesKey, RunParallelAction.DefaultBranchCount);
        var newCount = Math.Max(2, requested);
        var oldPorts = node.OutputPorts.ToList();
        var oldCount = oldPorts.Count;

        if (newCount == oldCount)
        {
            node.Config[RunParallelAction.BranchesKey] = newCount;
            IsDirty = true;
            return;
        }

        List<PortViewModel> newPorts;
        List<ConnectionViewModel> removed;
        if (newCount > oldCount)
        {
            newPorts = oldPorts.ToList();
            for (var i = oldCount; i < newCount; i++)
            {
                newPorts.Add(NodeViewModel.BranchOutputPort(i));
            }
            removed = new List<ConnectionViewModel>();
        }
        else
        {
            newPorts = oldPorts.Take(newCount).ToList();
            var dropped = oldPorts.Skip(newCount).ToHashSet();
            removed = Connections
                .Where(c => ReferenceEquals(c.Source, node) && dropped.Contains(c.SourcePort))
                .ToList();
        }

        _undo.Execute(new SetBranchCountCommand(this, node, oldPorts, newPorts, oldCount, newCount, removed));
        AfterEdit();
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
