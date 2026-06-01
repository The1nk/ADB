using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class NodeConfigRetryRoundTripTests
{
    private static BotEditorViewModel NewEditor()
    {
        var defs = new ActionRegistry();
        BuiltInActions.Register(defs, new ActionExecutorRegistry());
        return new BotEditorViewModel(defs);
    }

    [Fact]
    public void NewNode_HasEmptyConfig_AndDefaultRetry()
    {
        var node = NewEditor().AddNode("data.log", 0, 0);

        Assert.NotNull(node.Config);
        Assert.Empty(node.Config);
        Assert.Equal(1, node.RetryMaxAttempts);
        Assert.Equal(0, node.RetryDelayMs);
    }

    [Fact]
    public void SaveOpen_RoundTripsConfigValues()
    {
        var e = NewEditor();
        var node = e.AddNode("data.log", 5, 5);
        node.Config["message"] = "hello world";
        var path = Path.Combine(Path.GetTempPath(), $"adb-m4a-{Guid.NewGuid():N}.bot");

        try
        {
            e.Save(path);
            var reopened = NewEditor();
            reopened.Open(path);

            var loaded = reopened.Nodes.Single(n => n.TypeKey == "data.log");
            Assert.Equal("hello world", loaded.Config["message"]!.ToString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveOpen_RoundTripsRetry_WhenConfigured()
    {
        var e = NewEditor();
        var node = e.AddNode("data.log", 0, 0);
        node.RetryMaxAttempts = 5;
        node.RetryDelayMs = 500;
        var path = Path.Combine(Path.GetTempPath(), $"adb-m4a-{Guid.NewGuid():N}.bot");

        try
        {
            e.Save(path);
            var reopened = NewEditor();
            reopened.Open(path);

            var loaded = reopened.Nodes.Single(n => n.TypeKey == "data.log");
            Assert.Equal(5, loaded.RetryMaxAttempts);
            Assert.Equal(500, loaded.RetryDelayMs);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DefaultRetry_IsNotWrittenToBot()
    {
        var e = NewEditor();
        var node = e.AddNode("data.log", 0, 0);

        var bot = DocumentMapper.ToBot(e);

        Assert.Null(bot.Actions.Single(a => a.Id == node.Id).Retry);
    }
}
