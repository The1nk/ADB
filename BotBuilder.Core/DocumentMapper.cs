using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core.Connections;
using BotBuilder.Core.Targets;

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
            var action = new BotAction
            {
                Id = node.Id,
                TypeKey = node.TypeKey,
                Label = node.Label,
                TargetId = node.TargetId,
                CanvasPosition = new Position { X = node.X, Y = node.Y },
                Config = new Dictionary<string, object>(node.Config),
            };
            if (node.RetryMaxAttempts > 1)
            {
                action.Retry = new RetryPolicy { MaxAttempts = node.RetryMaxAttempts, DelayMs = node.RetryDelayMs };
            }
            bot.Actions.Add(action);
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

        foreach (var t in editor.TargetBar.Targets)
        {
            bot.Targets.Add(new BotTarget
            {
                Id = t.Id,
                Name = t.Name,
                Type = t.Type,
                Config = new Dictionary<string, string> { ["selector"] = t.Selector },
            });
        }

        return bot;
    }

    public static void Populate(BotEditorViewModel editor, Bot bot, ActionRegistry registry)
    {
        var nodes = bot.Actions.Select(a => BuildNode(a, registry)).ToList();
        editor.LoadFrom(bot.Id, bot.Name, nodes, placedNodes => BuildConnections(bot, placedNodes));

        editor.TargetBar.Targets.Clear();
        foreach (var t in bot.Targets)
        {
            editor.TargetBar.Targets.Add(new TargetViewModel
            {
                Id = t.Id,
                Name = t.Name,
                Type = t.Type,
                Selector = t.Config.TryGetValue("selector", out var sel) ? sel : string.Empty,
            });
        }

        editor.RefreshTargetBadges();
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
        NodeViewModel node;
        if (registry.TryGet(action.TypeKey, out var definition) && definition is not null)
        {
            node = NodeViewModel.FromDefinition(
                definition, action.Id, action.Label, action.CanvasPosition.X, action.CanvasPosition.Y);
        }
        else
        {
            node = new NodeViewModel(
                action.Id, action.TypeKey, action.Label, UnknownCategory,
                Array.Empty<PortViewModel>(), Array.Empty<PortViewModel>(),
                action.CanvasPosition.X, action.CanvasPosition.Y);
        }

        node.TargetId = action.TargetId;
        foreach (var kv in action.Config) { node.Config[kv.Key] = kv.Value; }
        if (node.TypeKey == RunParallelAction.RunParallelTypeKey)
        {
            var branches = System.Math.Max(2, ConfigValues.GetInt(node.Config, RunParallelAction.BranchesKey, RunParallelAction.DefaultBranchCount));
            node.SetBranchPortCount(branches);
        }
        node.RetryMaxAttempts = action.Retry?.MaxAttempts ?? 1;
        node.RetryDelayMs = action.Retry?.DelayMs ?? 0;
        return node;
    }
}
