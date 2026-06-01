using AdbCore.Actions;
using AdbCore.Models;
using BotBuilder.Core.Connections;

namespace BotBuilder.Core;

/// <summary>Maps between the persisted <see cref="Bot"/> model and the editor view-model.</summary>
public static class DocumentMapper
{
    private const string UnknownCategory = "Unknown";

    public static Bot ToBot(BotEditorViewModel editor)
    {
        var bot = new Bot { Id = editor.BotId, Name = editor.BotName };

        foreach (var node in editor.Nodes)
        {
            bot.Actions.Add(new BotAction
            {
                Id = node.Id,
                TypeKey = node.TypeKey,
                Label = node.Label,
                CanvasPosition = new Position { X = node.X, Y = node.Y },
            });
        }

        foreach (var c in editor.Connections)
        {
            bot.Connections.Add(new ActionConnection
            {
                Id = c.Id,
                SourceActionId = c.Source.Id,
                SourcePort = c.SourcePort.Name,
                TargetActionId = c.Target.Id,
                TargetPort = c.TargetPort.Name,
            });
        }

        return bot;
    }

    public static void Populate(BotEditorViewModel editor, Bot bot, ActionRegistry registry)
    {
        var nodes = bot.Actions.Select(a => BuildNode(a, registry)).ToList();
        editor.LoadFrom(bot.Id, bot.Name, nodes, placedNodes => BuildConnections(bot, placedNodes));
    }

    private static IEnumerable<ConnectionViewModel> BuildConnections(Bot bot, IReadOnlyList<NodeViewModel> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Id);

        foreach (var c in bot.Connections)
        {
            if (!byId.TryGetValue(c.SourceActionId, out var source) ||
                !byId.TryGetValue(c.TargetActionId, out var target))
            {
                continue;
            }

            var sourcePort = source.OutputPorts.FirstOrDefault(p => p.Name == c.SourcePort);
            var targetPort = target.InputPorts.FirstOrDefault(p => p.Name == c.TargetPort);
            if (sourcePort is null || targetPort is null)
            {
                continue;
            }

            yield return new ConnectionViewModel(c.Id, source, sourcePort, target, targetPort);
        }
    }

    private static NodeViewModel BuildNode(BotAction action, ActionRegistry registry)
    {
        if (registry.TryGet(action.TypeKey, out var definition) && definition is not null)
        {
            return NodeViewModel.FromDefinition(
                definition, action.Id, action.Label, action.CanvasPosition.X, action.CanvasPosition.Y);
        }

        return new NodeViewModel(
            action.Id, action.TypeKey, action.Label, UnknownCategory,
            Array.Empty<PortViewModel>(), Array.Empty<PortViewModel>(),
            action.CanvasPosition.X, action.CanvasPosition.Y);
    }
}
