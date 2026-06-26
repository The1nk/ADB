using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;
using AdbCore.Serialization;
using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class TemplateEmbedRoundTripTests
{
    private static string WriteTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tmpl_{Guid.NewGuid():N}.png");
        using var bmp = new Bitmap(4, 4);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void OpenPathBasedBot_ThenSave_EmbedsImageAndStripsPathToBasename()
    {
        var png = WriteTempPng();
        var srcBotPath = Path.Combine(Path.GetTempPath(), $"src_{Guid.NewGuid():N}.bot");
        var outBotPath = Path.Combine(Path.GetTempPath(), $"out_{Guid.NewGuid():N}.bot");
        try
        {
            var bot = new Bot { Name = "T" };
            bot.Actions.Add(new BotAction
            {
                Id = Guid.NewGuid(),
                TypeKey = "screen.findImage",
                Config = { [TemplateMatchCore.TemplatePathKey] = png },
            });
            new BotSerializer().Save(bot, srcBotPath);

            // Editor construction mirrors the existing editor tests (see BotEditorViewModelSaveTests.cs).
            var defs = new AdbCore.Actions.ActionRegistry();
            AdbCore.Actions.BuiltIn.BuiltInActions.Register(defs, new AdbCore.Execution.ActionExecutorRegistry());
            var editor = new BotEditorViewModel(defs);
            editor.Open(srcBotPath);
            editor.Save(outBotPath);

            var reloaded = new BotSerializer().Load(outBotPath);
            var cfg = reloaded.Actions[0].Config;
            Assert.True(cfg.ContainsKey(TemplateMatchCore.TemplateImageKey));
            Assert.Equal(Path.GetFileName(png),
                AdbCore.Actions.ConfigValues.AsString(cfg[TemplateMatchCore.TemplatePathKey]));
        }
        finally
        {
            foreach (var p in new[] { png, srcBotPath, outBotPath }) { try { File.Delete(p); } catch { } }
        }
    }
}
