using System.IO;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;

namespace BotBuilder.Core;

/// <summary>Ensures every image action carries its template as embedded base64 (so a .bot is
/// self-contained) and migrates legacy file-path templates: embed a still-present source one last time,
/// derive the display <c>templateName</c> from its basename, and drop the obsolete <c>templatePath</c>.</summary>
public static class TemplateEmbedder
{
    /// <summary>Reads a file's bytes, or null when the path is empty or the file does not exist.</summary>
    public static byte[]? ReadFileIfExists(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllBytes(path) : null;

    /// <summary>Embeds not-yet-embedded templates from a still-readable source path, sets a display
    /// <c>templateName</c> from the legacy basename when absent, and removes <c>templatePath</c>. Recurses
    /// into nested bots. Idempotent. Mutates and returns the bot.</summary>
    public static Bot Migrate(Bot bot, Func<string?, byte[]?> read)
    {
        foreach (var action in bot.Actions)
        {
            var path = Get(action, TemplateMatchCore.TemplatePathKey);
            var hasImage = !string.IsNullOrWhiteSpace(Get(action, TemplateMatchCore.TemplateImageKey));

            if (!hasImage && read(path) is byte[] bytes)
            {
                action.Config[TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(bytes);
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                if (string.IsNullOrWhiteSpace(Get(action, TemplateMatchCore.TemplateNameKey)))
                {
                    action.Config[TemplateMatchCore.TemplateNameKey] = Path.GetFileName(path);
                }
                action.Config.Remove(TemplateMatchCore.TemplatePathKey);
            }
        }

        foreach (var nested in bot.NestedBots)
        {
            Migrate(nested, read);
        }

        return bot;
    }

    /// <summary>Save-time normalization: same as <see cref="Migrate"/> (capture embeds directly, so there
    /// is nothing extra to do). Kept as a distinct entry point for the save path.</summary>
    public static Bot PrepareForSave(Bot bot, Func<string?, byte[]?> read) => Migrate(bot, read);

    private static string Get(BotAction action, string key)
        => action.Config.TryGetValue(key, out var v) ? ConfigValues.AsString(v) : string.Empty;
}
