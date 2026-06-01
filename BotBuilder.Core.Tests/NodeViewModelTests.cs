using System.ComponentModel;
using AdbCore.Actions.BuiltIn;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeViewModelTests
{
    [Fact]
    public void FromDefinition_DerivesTypeKeyCategoryAndPorts()
    {
        var node = NodeViewModel.FromDefinition(new LogAction(), Guid.NewGuid(), label: "", x: 10, y: 20);

        Assert.Equal("data.log", node.TypeKey);
        Assert.Equal("Data", node.Category);
        Assert.Equal("Log", node.Label);
        Assert.Single(node.InputPorts);
        Assert.Equal(PortDirection.In, node.InputPorts[0].Direction);
        Assert.Single(node.OutputPorts);
        Assert.Equal(PortDirection.Out, node.OutputPorts[0].Direction);
        Assert.Equal(10, node.X);
        Assert.Equal(20, node.Y);
    }

    [Fact]
    public void FromDefinition_KeepsExplicitLabel()
    {
        var node = NodeViewModel.FromDefinition(new StartAction(), Guid.NewGuid(), label: "Begin", x: 0, y: 0);

        Assert.Equal("Begin", node.Label);
        Assert.Empty(node.InputPorts);
        Assert.Single(node.OutputPorts);
    }

    [Fact]
    public void CategoryColor_MatchesCategoryColors()
    {
        var node = NodeViewModel.FromDefinition(new StartAction(), Guid.NewGuid(), "", 0, 0);

        Assert.Equal(CategoryColors.ColorFor("Control Flow"), node.CategoryColor);
    }

    [Fact]
    public void SettingX_RaisesPropertyChanged()
    {
        var node = NodeViewModel.FromDefinition(new StartAction(), Guid.NewGuid(), "", 0, 0);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        node.X = 99;

        Assert.Contains(nameof(NodeViewModel.X), raised);
    }
}
