using System.Text.Json;
using AdbCore.Actions;
using BotBuilder.Core;
using BotBuilder.Core.Properties;
using Xunit;

namespace BotBuilder.Core.Tests;

public class ConfigFieldViewModelTests
{
    private static NodeViewModel Node()
        => new(Guid.NewGuid(), "x", "X", "Test", Array.Empty<PortViewModel>(), Array.Empty<PortViewModel>(), 0, 0);

    private static ConfigFieldViewModel Field(NodeViewModel node, ConfigFieldType type, object? @default = null, params string[] options)
        => new(node, new ConfigField { Key = "k", Label = "K", Type = type, DefaultValue = @default, Options = options.ToList() }, () => { });

    [Fact]
    public void String_AbsentKey_ReturnsDefault()
    {
        var f = Field(Node(), ConfigFieldType.String, @default: "fallback");

        Assert.Equal("fallback", f.Value);
    }

    [Fact]
    public void String_Set_StoresStringInConfig()
    {
        var node = Node();
        var f = Field(node, ConfigFieldType.String);

        f.Value = "hi";

        Assert.Equal("hi", node.Config["k"]);
    }

    [Fact]
    public void Number_SetFromString_StoresDouble()
    {
        var node = Node();
        var f = Field(node, ConfigFieldType.Number);

        f.Value = "0.9";

        Assert.Equal(0.9, Assert.IsType<double>(node.Config["k"]));
    }

    [Fact]
    public void Number_FromJsonElement_ReadsAsDouble()
    {
        var node = Node();
        node.Config["k"] = JsonDocument.Parse("0.75").RootElement;
        var f = Field(node, ConfigFieldType.Number);

        Assert.Equal(0.75, Assert.IsType<double>(f.Value));
    }

    [Fact]
    public void Boolean_SetTrue_StoresBool_AndReadsBackFromJsonElement()
    {
        var node = Node();
        var f = Field(node, ConfigFieldType.Boolean);

        f.Value = true;
        Assert.True(Assert.IsType<bool>(node.Config["k"]));

        node.Config["k"] = JsonDocument.Parse("true").RootElement;
        Assert.True(Assert.IsType<bool>(f.Value));
    }

    [Fact]
    public void Set_InvokesOnChanged()
    {
        var node = Node();
        var changed = 0;
        var f = new ConfigFieldViewModel(node, new ConfigField { Key = "k", Type = ConfigFieldType.String }, () => changed++);

        f.Value = "z";

        Assert.Equal(1, changed);
    }

    [Fact]
    public void Number_SetExpression_PreservesStringInConfig()
    {
        var node = Node();
        var f = Field(node, ConfigFieldType.Number);

        f.Value = "${matchRandX}";

        Assert.Equal("${matchRandX}", Assert.IsType<string>(node.Config["k"]));
    }

    [Fact]
    public void Number_GetExpression_ReturnsStringNotZero()
    {
        var node = Node();
        node.Config["k"] = "${matchRandX}";
        var f = Field(node, ConfigFieldType.Number);

        Assert.Equal("${matchRandX}", f.Value);
    }

    [Fact]
    public void Number_ExpressionFromJsonElement_Preserved()
    {
        var node = Node();
        node.Config["k"] = JsonDocument.Parse("\"${matchCenterX}\"").RootElement;
        var f = Field(node, ConfigFieldType.Number);

        Assert.Equal("${matchCenterX}", f.Value);
    }

    [Fact]
    public void Number_LiteralString_StillStoresDouble()
    {
        var node = Node();
        var f = Field(node, ConfigFieldType.Number);

        f.Value = "120";

        Assert.Equal(120d, Assert.IsType<double>(node.Config["k"]));
    }
}
