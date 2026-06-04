using System.Collections.Generic;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class DocumentMapperBranchesTests
{
    [Fact]
    public void Load_RunParallelWithFiveBranches_RebuildsPortsAndLinksConnections()
    {
        var registry = new ActionRegistry();
        BuiltInActions.Register(registry, new ActionExecutorRegistry());

        var rpId = System.Guid.NewGuid();
        var endId = System.Guid.NewGuid();
        var bot = new Bot { Id = System.Guid.NewGuid(), Name = "p" };
        bot.Actions.Add(new BotAction
        {
            Id = rpId, TypeKey = RunParallelAction.RunParallelTypeKey, Label = "RP",
            Config = new Dictionary<string, object> { [RunParallelAction.BranchesKey] = 5 },
            CanvasPosition = new Position { X = 0, Y = 0 },
        });
        bot.Actions.Add(new BotAction { Id = endId, TypeKey = "control.end", Label = "End", CanvasPosition = new Position { X = 200, Y = 0 } });
        bot.Connections.Add(new ActionConnection
        {
            Id = System.Guid.NewGuid(), SourceActionId = rpId, SourcePort = "branch5", TargetActionId = endId, TargetPort = "in",
        });

        var editor = new BotEditorViewModel(registry);
        DocumentMapper.Populate(editor, bot, registry);

        var rp = editor.Nodes.Single(n => n.Id == rpId);
        Assert.Equal(5, rp.OutputPorts.Count);
        Assert.Single(editor.Connections);
        Assert.Equal("branch5", editor.Connections[0].SourcePort.Name);
    }
}
