using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class ConnectionValidatorTests
{
    private static NodeViewModel Log() => NodeViewModel.FromDefinition(new LogAction(), Guid.NewGuid(), "", 0, 0);

    private static ConnectionViewModel Edge(NodeViewModel s, NodeViewModel t)
        => new(Guid.NewGuid(), s, s.OutputPorts[0], t, t.InputPorts[0]);

    [Fact]
    public void Valid_OutputToInput_IsAllowed()
    {
        var a = Log();
        var b = Log();

        var result = ConnectionValidator.Validate(Array.Empty<ConnectionViewModel>(),
            a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.None, result);
    }

    [Fact]
    public void OutputToOutput_IsRejected()
    {
        var a = Log();
        var b = Log();

        var result = ConnectionValidator.Validate(Array.Empty<ConnectionViewModel>(),
            a, a.OutputPorts[0], b, b.OutputPorts[0]);

        Assert.Equal(ConnectionError.NotOutputToInput, result);
    }

    [Fact]
    public void SelfConnection_IsRejected()
    {
        var a = Log();

        var result = ConnectionValidator.Validate(Array.Empty<ConnectionViewModel>(),
            a, a.OutputPorts[0], a, a.InputPorts[0]);

        Assert.Equal(ConnectionError.SelfConnection, result);
    }

    [Fact]
    public void DuplicateEdge_IsRejected()
    {
        var a = Log();
        var b = Log();
        var existing = new[] { Edge(a, b) };

        var result = ConnectionValidator.Validate(existing,
            a, a.OutputPorts[0], b, b.InputPorts[0]);

        Assert.Equal(ConnectionError.Duplicate, result);
    }

    [Fact]
    public void OutputPort_WithExistingConnection_RejectsSecondEdge()
    {
        var a = Log();
        var b = Log();
        var c = Log();
        var existing = new[] { Edge(a, b) };

        var result = ConnectionValidator.Validate(existing,
            a, a.OutputPorts[0], c, c.InputPorts[0]);

        Assert.Equal(ConnectionError.SourcePortOccupied, result);
    }

    [Fact]
    public void FanIn_MultipleOutputsIntoOneInputPort_IsAllowed()
    {
        var a = Log();
        var b = Log();
        var c = Log();
        var existing = new[] { Edge(a, c) }; // a.out -> c.in already

        // b.out -> c.in : a different source into the SAME input port (convergence) is allowed.
        var result = ConnectionValidator.Validate(existing,
            b, b.OutputPorts[0], c, c.InputPorts[0]);

        Assert.Equal(ConnectionError.None, result);
    }

    [Fact]
    public void Cycle_IsRejected()
    {
        var a = Log();
        var b = Log();
        var c = Log();
        var existing = new[] { Edge(a, b), Edge(b, c) };

        var result = ConnectionValidator.Validate(existing,
            c, c.OutputPorts[0], a, a.InputPorts[0]);

        Assert.Equal(ConnectionError.WouldCreateCycle, result);
    }

    [Fact]
    public void NonCycle_ParallelPath_IsAllowed()
    {
        var a = Log();
        var b = Log();
        var c = Log();
        var existing = new[] { Edge(a, b), Edge(a, c) };

        var result = ConnectionValidator.Validate(existing,
            b, b.OutputPorts[0], c, c.InputPorts[0]);

        Assert.Equal(ConnectionError.None, result);
    }
}
