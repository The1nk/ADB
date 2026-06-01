using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class DocumentMapperTargetTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void ToBot_WritesTargets_WithSelectorInConfig()
    {
        var e = NewEditor();
        var t = e.TargetBar.AddTarget();
        t.Name = "Client 1";
        t.Type = BotTargetType.AndroidDevice;
        t.Selector = "serial:emulator-5554";

        var bot = DocumentMapper.ToBot(e);

        var target = Assert.Single(bot.Targets);
        Assert.Equal(t.Id, target.Id);
        Assert.Equal("Client 1", target.Name);
        Assert.Equal(BotTargetType.AndroidDevice, target.Type);
        Assert.Equal("serial:emulator-5554", target.Config["selector"]);
    }

    [Fact]
    public void ToBot_WritesNodeTargetId()
    {
        var e = NewEditor();
        var node = e.AddNode("control.start", 0, 0);
        var t = e.TargetBar.AddTarget();
        e.TargetBar.AddTarget();
        e.AssignTarget(node, t.Id);

        var bot = DocumentMapper.ToBot(e);

        Assert.Equal(t.Id, bot.Actions.Single(a => a.Id == node.Id).TargetId);
    }

    [Fact]
    public void SaveOpen_RoundTripsTargetsAndAssignment()
    {
        var e = NewEditor();
        var node = e.AddNode("data.log", 10, 10);
        var t1 = e.TargetBar.AddTarget();
        t1.Name = "Client 1";
        t1.Selector = "process:BlueStacks";
        var t2 = e.TargetBar.AddTarget();
        t2.Name = "My Phone";
        e.AssignTarget(node, t2.Id);
        var path = Path.Combine(Path.GetTempPath(), $"adb-m3c2-{Guid.NewGuid():N}.bot");

        try
        {
            e.Save(path);
            var reopened = NewEditor();
            reopened.Open(path);

            Assert.Equal(2, reopened.TargetBar.Targets.Count);
            var phone = reopened.TargetBar.Targets.Single(x => x.Name == "My Phone");
            var client = reopened.TargetBar.Targets.Single(x => x.Name == "Client 1");
            Assert.Equal("process:BlueStacks", client.Selector);

            var loadedNode = reopened.Nodes.Single(n => n.TypeKey == "data.log");
            Assert.Equal(phone.Id, loadedNode.TargetId);
            Assert.Equal("My Phone", loadedNode.TargetBadge);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
