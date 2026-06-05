using System.ComponentModel;
using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class ConnectionViewModelTests
{
    private static (NodeViewModel src, NodeViewModel tgt) TwoNodes()
        => (NodeViewModel.FromDefinition(new StartAction(), Guid.NewGuid(), "", 0, 0),
            NodeViewModel.FromDefinition(new LogAction(), Guid.NewGuid(), "", 300, 100));

    [Fact]
    public void PathData_ReflectsEndpointAnchors()
    {
        var (src, tgt) = TwoNodes();
        var conn = new ConnectionViewModel(Guid.NewGuid(), src, src.OutputPorts[0], tgt, tgt.InputPorts[0]);

        var expectedStartX = src.X + src.OutputPorts[0].AnchorOffset.X;
        Assert.Contains($"M {expectedStartX},", conn.PathData);
    }

    [Fact]
    public void MovingSourceNode_RaisesPathDataChanged()
    {
        var (src, tgt) = TwoNodes();
        var conn = new ConnectionViewModel(Guid.NewGuid(), src, src.OutputPorts[0], tgt, tgt.InputPorts[0]);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)conn).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        src.X += 25;

        Assert.Contains(nameof(ConnectionViewModel.PathData), raised);
    }

    [Fact]
    public void HeightChange_RecomputesPathData()
    {
        var (src, tgt) = TwoNodes();
        var conn = new ConnectionViewModel(Guid.NewGuid(), src, src.OutputPorts[0], tgt, tgt.InputPorts[0]);
        var raised = false;
        conn.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ConnectionViewModel.PathData)) raised = true; };

        src.Height += 40;   // simulate a Run Parallel re-center

        Assert.True(raised);
    }
}
