using System.Text.Json.Nodes;
using AdbCore.Models;
using AdbCore.Serialization;
using BotRunner;
using Xunit;

namespace BotRunner.Tests;

public class CliIntegrationTests
{
    private static string WriteBot(Bot bot)
    {
        var path = Path.Combine(Path.GetTempPath(), $"adb-m2-{Guid.NewGuid():N}.bot");
        new BotSerializer().Save(bot, path);
        return path;
    }

    private static Bot StartLogEndBot(string message)
    {
        var startId = Guid.NewGuid();
        var logId = Guid.NewGuid();
        var endId = Guid.NewGuid();
        var bot = new Bot { Name = "hello" };
        bot.Actions.Add(new BotAction { Id = startId, TypeKey = "control.start", Label = "Start" });
        var log = new BotAction { Id = logId, TypeKey = "data.log", Label = "Log" };
        log.Config["message"] = message;
        bot.Actions.Add(log);
        bot.Actions.Add(new BotAction { Id = endId, TypeKey = "control.end", Label = "End" });
        bot.Connections.Add(new ActionConnection { Id = Guid.NewGuid(), SourceActionId = startId, SourcePort = "out", TargetActionId = logId, TargetPort = "in" });
        bot.Connections.Add(new ActionConnection { Id = Guid.NewGuid(), SourceActionId = logId, SourcePort = "out", TargetActionId = endId, TargetPort = "in" });
        return bot;
    }

    [Fact]
    public async Task RunAsync_SimpleBot_Succeeds_LogsMessage_ExitsZero()
    {
        var botPath = WriteBot(StartLogEndBot("hello from M2"));
        var logPath = Path.ChangeExtension(botPath, ".log");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await Cli.RunAsync(new[] { "--bot", botPath }, stdout, stderr, default);

            Assert.Equal(0, exit);
            var logText = await File.ReadAllTextAsync(logPath);
            Assert.Contains("hello from M2", logText);
            foreach (var line in logText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.NotNull(JsonNode.Parse(line));
            }
            Assert.Contains("run-end", logText);
        }
        finally
        {
            File.Delete(botPath);
            if (File.Exists(logPath)) File.Delete(logPath);
        }
    }

    [Fact]
    public async Task RunAsync_MissingBotFile_ExitsTwo()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await Cli.RunAsync(new[] { "--bot", @"C:\nope\does-not-exist.bot" }, stdout, stderr, default);

        Assert.Equal(2, exit);
        Assert.Contains("not found", stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_DeclaredTargetMissingArg_ExitsTwo()
    {
        var bot = StartLogEndBot("hi");
        bot.Targets.Add(new BotTarget { Id = Guid.NewGuid(), Name = "Client 1", Type = BotTargetType.Window });
        var botPath = WriteBot(bot);
        var logPath = Path.ChangeExtension(botPath, ".log");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await Cli.RunAsync(new[] { "--bot", botPath }, stdout, stderr, default);

            Assert.Equal(2, exit);
            Assert.Contains("Client 1", stderr.ToString());
        }
        finally
        {
            File.Delete(botPath);
            if (File.Exists(logPath)) File.Delete(logPath);
        }
    }

    [Fact]
    public async Task RunAsync_BadArguments_ExitsTwo()
    {
        var exit = await Cli.RunAsync(Array.Empty<string>(), new StringWriter(), new StringWriter(), default);

        Assert.Equal(2, exit);
    }
}
