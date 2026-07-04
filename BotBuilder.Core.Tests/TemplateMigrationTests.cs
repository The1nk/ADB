using System;
using System.Collections.Generic;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class TemplateMigrationTests
{
    private static Bot BotWith(Dictionary<string, object> config)
        => new() { Actions = { new BotAction { TypeKey = "screen.findImage", Config = config } } };

    [Fact]
    public void Migrate_PathOnly_EmbedsImage_SetsNameFromBasename_RemovesPath()
    {
        var bot = BotWith(new() { [TemplateMatchCore.TemplatePathKey] = @"C:\x\attack.png" });
        var read = (string? p) => p == @"C:\x\attack.png" ? new byte[] { 1, 2, 3 } : null;

        TemplateEmbedder.Migrate(bot, read);

        var config = bot.Actions[0].Config;
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), config[TemplateMatchCore.TemplateImageKey]);
        Assert.Equal("attack.png", config[TemplateMatchCore.TemplateNameKey]);
        Assert.False(config.ContainsKey(TemplateMatchCore.TemplatePathKey));
    }

    [Fact]
    public void Migrate_AlreadyEmbedded_LeavesImageUnchanged_SetsNameFromBasename_RemovesPath()
    {
        var bot = BotWith(new()
        {
            [TemplateMatchCore.TemplateImageKey] = "AAAA",
            [TemplateMatchCore.TemplatePathKey] = @"C:\x\loot.png",
        });

        TemplateEmbedder.Migrate(bot, _ => null);

        var config = bot.Actions[0].Config;
        Assert.Equal("loot.png", config[TemplateMatchCore.TemplateNameKey]);
        Assert.Equal("AAAA", config[TemplateMatchCore.TemplateImageKey]);
        Assert.False(config.ContainsKey(TemplateMatchCore.TemplatePathKey));
    }

    [Fact]
    public void Migrate_MissingFile_NoImageKey_SetsNameFromBasename_RemovesPath()
    {
        var bot = BotWith(new() { [TemplateMatchCore.TemplatePathKey] = @"C:\x\gone.png" });

        TemplateEmbedder.Migrate(bot, _ => null);

        var config = bot.Actions[0].Config;
        Assert.False(config.ContainsKey(TemplateMatchCore.TemplateImageKey));
        Assert.Equal("gone.png", config[TemplateMatchCore.TemplateNameKey]);
        Assert.False(config.ContainsKey(TemplateMatchCore.TemplatePathKey));
    }

    [Fact]
    public void Migrate_NestedBotAction_MigratedRecursively()
    {
        var nested = new Bot();
        nested.Actions.Add(new BotAction
        {
            TypeKey = "screen.findImage",
            Config = new() { [TemplateMatchCore.TemplatePathKey] = @"C:\x\attack.png" },
        });
        var parent = new Bot();
        parent.NestedBots.Add(nested);
        var read = (string? p) => p == @"C:\x\attack.png" ? new byte[] { 1, 2, 3 } : null;

        TemplateEmbedder.Migrate(parent, read);

        var config = nested.Actions[0].Config;
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), config[TemplateMatchCore.TemplateImageKey]);
        Assert.Equal("attack.png", config[TemplateMatchCore.TemplateNameKey]);
        Assert.False(config.ContainsKey(TemplateMatchCore.TemplatePathKey));
    }

    [Fact]
    public void Migrate_ExistingTemplateName_NotOverwritten_PathStillRemoved()
    {
        var bot = BotWith(new()
        {
            [TemplateMatchCore.TemplateNameKey] = "Custom Label",
            [TemplateMatchCore.TemplatePathKey] = @"C:\x\attack.png",
        });
        var read = (string? p) => p == @"C:\x\attack.png" ? new byte[] { 1, 2, 3 } : null;

        TemplateEmbedder.Migrate(bot, read);

        var config = bot.Actions[0].Config;
        Assert.Equal("Custom Label", config[TemplateMatchCore.TemplateNameKey]);
        Assert.False(config.ContainsKey(TemplateMatchCore.TemplatePathKey));
    }
}
