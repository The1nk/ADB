using AdbCore.Actions;
using AdbCore.Models;

namespace BotBuilder.Core;

/// <summary>Maps between the persisted <see cref="Bot"/> model and the editor view-model.</summary>
public static class DocumentMapper
{
    private const string UnknownCategory = "Unknown";

    /// <summary>Assembles a <see cref="Bot"/> from the current editor state (M3a: nodes only).</summary>
    public static Bot ToBot(BotEditorViewModel editor)
    {
        var bot = new Bot
        {
            Id = editor.BotId,
            Name = editor.BotName,
        };

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

        return bot;
    }

    /// <summary>Replaces the editor's contents with nodes built from <paramref name="bot"/>.</summary>
    public static void Populate(BotEditorViewModel editor, Bot bot, ActionRegistry registry)
    {
        editor.LoadFrom(bot.Id, bot.Name, bot.Actions.Select(a => BuildNode(a, registry)));
    }

    private static NodeViewModel BuildNode(BotAction action, ActionRegistry registry)
    {
        if (registry.TryGet(action.TypeKey, out var definition) && definition is not null)
        {
            return NodeViewModel.FromDefinition(
                definition, action.Id, action.Label, action.CanvasPosition.X, action.CanvasPosition.Y);
        }

        return new NodeViewModel(
            action.Id,
            action.TypeKey,
            action.Label,
            UnknownCategory,
            Array.Empty<PortViewModel>(),
            Array.Empty<PortViewModel>(),
            action.CanvasPosition.X,
            action.CanvasPosition.Y);
    }
}
