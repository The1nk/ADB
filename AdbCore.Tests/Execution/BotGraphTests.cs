using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class BotGraphTests
{
    private static BotAction Node(string typeKey, out Guid id)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    [Fact]
    public void EntryPoint_IsFirstActionWithNoIncomingEdge()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var bot = new Bot();
        bot.Actions.AddRange(new[] { a, b });
        bot.Connections.Add(Edge(aId, "out", bId));

        var graph = new BotGraph(bot);

        Assert.Same(a, graph.EntryPoint);
    }

    [Fact]
    public void EntryPoint_NullWhenEveryActionHasIncoming()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var bot = new Bot();
        bot.Actions.AddRange(new[] { a, b });
        bot.Connections.Add(Edge(aId, "out", bId));
        bot.Connections.Add(Edge(bId, "out", aId));

        Assert.Null(new BotGraph(bot).EntryPoint);
    }

    [Fact]
    public void FindNext_ReturnsTargetForWiredPort_NullForUnwired()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var bot = new Bot();
        bot.Actions.AddRange(new[] { a, b });
        bot.Connections.Add(Edge(aId, "out", bId));

        var graph = new BotGraph(bot);

        Assert.Same(b, graph.FindNext(aId, "out"));
        Assert.Null(graph.FindNext(aId, "onFailure"));
        Assert.Null(graph.FindNext(bId, "out"));
    }

    [Fact]
    public void FindNext_FirstConnectionWins_WhenPortDuplicated()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var c = Node("c", out var cId);
        var bot = new Bot();
        bot.Actions.AddRange(new[] { a, b, c });
        bot.Connections.Add(Edge(aId, "out", bId)); // first in document order
        bot.Connections.Add(Edge(aId, "out", cId));

        Assert.Same(b, new BotGraph(bot).FindNext(aId, "out"));
    }

    [Fact]
    public void Find_ReturnsActionById_NullWhenAbsent()
    {
        var a = Node("a", out var aId);
        var bot = new Bot();
        bot.Actions.Add(a);

        var graph = new BotGraph(bot);

        Assert.Same(a, graph.Find(aId));
        Assert.Null(graph.Find(Guid.NewGuid()));
    }

    [Fact]
    public void Outgoing_ReturnsEdgesFromAction_EmptyWhenNone()
    {
        var a = Node("a", out var aId);
        var b = Node("b", out var bId);
        var bot = new Bot();
        bot.Actions.AddRange(new[] { a, b });
        bot.Connections.Add(Edge(aId, "out", bId));

        var graph = new BotGraph(bot);

        Assert.Single(graph.Outgoing(aId));
        Assert.Empty(graph.Outgoing(bId));
    }
}
