using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class PropertiesViewModelTests
{
    private static BotEditorViewModel BuiltInEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    private static BotEditorViewModel ConfigurableEditor()
    {
        var defs = new ActionRegistry();
        defs.Register(new FakeConfigurableDefinition());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void NoSelection_HasNoNode_NoFields()
    {
        var e = BuiltInEditor();

        Assert.Null(e.Properties.Node);
        Assert.Empty(e.Properties.Fields);
        Assert.False(e.Properties.SupportsRetry);
    }

    [Fact]
    public void SelectingLogNode_ExposesItsMessageField()
    {
        var e = BuiltInEditor();
        var node = e.AddNode("data.log", 0, 0);

        e.Select(node);

        Assert.Same(node, e.Properties.Node);
        Assert.Equal("Log", e.Properties.ActionTitle);
        var field = Assert.Single(e.Properties.Fields);
        Assert.Equal("message", field.Key);
        Assert.False(e.Properties.SupportsRetry);
    }

    [Fact]
    public void Deselecting_ClearsFields()
    {
        var e = BuiltInEditor();
        e.Select(e.AddNode("data.log", 0, 0));

        e.Select(null);

        Assert.Null(e.Properties.Node);
        Assert.Empty(e.Properties.Fields);
    }

    [Fact]
    public void ConfigurableAction_ExposesAllFields_AndSupportsRetry()
    {
        var e = ConfigurableEditor();
        var node = e.AddNode("test.configurable", 0, 0);

        e.Select(node);

        Assert.Equal(3, e.Properties.Fields.Count);
        Assert.True(e.Properties.SupportsRetry);
    }

    [Fact]
    public void SelectedTargetId_Setter_AssignsTarget()
    {
        var e = BuiltInEditor();
        var node = e.AddNode("data.log", 0, 0);
        e.TargetBar.AddTarget();
        var t2 = e.TargetBar.AddTarget();
        e.Select(node);

        e.Properties.SelectedTargetId = t2.Id;

        Assert.Equal(t2.Id, node.TargetId);
    }

    [Fact]
    public void SupportsCoordinatePicking_TrueForCoordinateActions_FalseOtherwise()
    {
        var e = BuiltInEditor();
        var tapNode = e.AddNode("android.tap", 0, 0);
        var logNode = e.AddNode("data.log", 0, 0);

        e.Select(tapNode);
        Assert.True(e.Properties.SupportsCoordinatePicking);

        e.Select(logNode);
        Assert.False(e.Properties.SupportsCoordinatePicking);
    }

    [Fact]
    public void EditingAField_MarksEditorDirty()
    {
        var e = BuiltInEditor();
        var node = e.AddNode("data.log", 0, 0);
        e.Select(node);
        var path = Path.Combine(Path.GetTempPath(), $"adb-{Guid.NewGuid():N}.bot");
        e.Save(path); // clears IsDirty

        try
        {
            e.Properties.Fields[0].Value = "edited";
            Assert.True(e.IsDirty);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
