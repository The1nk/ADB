using System;
using System.Collections.Generic;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class TemplateEmbedderTests
{
    private static Bot BotWith(Dictionary<string, object> config)
        => new() { Actions = { new BotAction { TypeKey = "screen.findImage", Config = config } } };

    [Fact]
    public void Embed_FillsTemplateImageFromReadableFile_LeavesPath()
    {
        var bot = BotWith(new() { [TemplateMatchCore.TemplatePathKey] = @"C:\caps\btn.png" });
        var read = (string? p) => p == @"C:\caps\btn.png" ? new byte[] { 1, 2, 3 } : null;

        TemplateEmbedder.Embed(bot, read);

        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            bot.Actions[0].Config[TemplateMatchCore.TemplateImageKey]);
        Assert.Equal(@"C:\caps\btn.png", bot.Actions[0].Config[TemplateMatchCore.TemplatePathKey]);
    }

    [Fact]
    public void Embed_Idempotent_DoesNotOverwriteExistingImage()
    {
        var bot = BotWith(new()
        {
            [TemplateMatchCore.TemplatePathKey] = @"C:\caps\btn.png",
            [TemplateMatchCore.TemplateImageKey] = "ALREADY",
        });

        TemplateEmbedder.Embed(bot, _ => new byte[] { 9 });

        Assert.Equal("ALREADY", bot.Actions[0].Config[TemplateMatchCore.TemplateImageKey]);
    }

    [Fact]
    public void Embed_MissingFile_LeavesNoImage()
    {
        var bot = BotWith(new() { [TemplateMatchCore.TemplatePathKey] = @"C:\gone.png" });

        TemplateEmbedder.Embed(bot, _ => null);

        Assert.False(bot.Actions[0].Config.ContainsKey(TemplateMatchCore.TemplateImageKey));
    }

    [Fact]
    public void PrepareForSave_EmbedsThenStripsPathToBasename()
    {
        var bot = BotWith(new() { [TemplateMatchCore.TemplatePathKey] = @"C:\caps\sub\btn.png" });

        TemplateEmbedder.PrepareForSave(bot, _ => new byte[] { 1 });

        Assert.Equal("btn.png", bot.Actions[0].Config[TemplateMatchCore.TemplatePathKey]);
        Assert.True(bot.Actions[0].Config.ContainsKey(TemplateMatchCore.TemplateImageKey));
    }

    [Fact]
    public void PrepareForSave_NoEmbeddableImage_LeavesPathUnchanged()
    {
        var bot = BotWith(new() { [TemplateMatchCore.TemplatePathKey] = @"C:\gone.png" });

        TemplateEmbedder.PrepareForSave(bot, _ => null);

        Assert.Equal(@"C:\gone.png", bot.Actions[0].Config[TemplateMatchCore.TemplatePathKey]);
    }
}
