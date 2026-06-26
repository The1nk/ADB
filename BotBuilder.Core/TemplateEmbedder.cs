using System.IO;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;

namespace BotBuilder.Core;

/// <summary>Embeds image-action templates into the bot model as base64 (so a saved .bot is self-contained)
/// and, on save, strips the source path to its basename. Pure; the file reader is injected for testing.</summary>
public static class TemplateEmbedder
{
    /// <summary>Reads a file's bytes, or null when the path is empty or the file does not exist.</summary>
    public static byte[]? ReadFileIfExists(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllBytes(path) : null;

    /// <summary>For each action with a source path but no embedded image yet, reads the file and stores its
    /// base64 under the templateImage key. Idempotent; leaves the path untouched. Mutates and returns the bot.</summary>
    public static Bot Embed(Bot bot, Func<string?, byte[]?> read)
    {
        foreach (var action in bot.Actions)
        {
            if (!string.IsNullOrWhiteSpace(Get(action, TemplateMatchCore.TemplateImageKey)))
            {
                continue;
            }

            if (read(Get(action, TemplateMatchCore.TemplatePathKey)) is byte[] bytes)
            {
                action.Config[TemplateMatchCore.TemplateImageKey] = Convert.ToBase64String(bytes);
            }
        }

        return bot;
    }

    /// <summary>Embeds any not-yet-embedded templates, then rewrites the source path to its basename for every
    /// action that now carries embedded bytes. Mutates and returns the bot.</summary>
    public static Bot PrepareForSave(Bot bot, Func<string?, byte[]?> read)
    {
        Embed(bot, read);

        foreach (var action in bot.Actions)
        {
            if (string.IsNullOrWhiteSpace(Get(action, TemplateMatchCore.TemplateImageKey)))
            {
                continue;
            }

            var path = Get(action, TemplateMatchCore.TemplatePathKey);
            if (!string.IsNullOrWhiteSpace(path))
            {
                action.Config[TemplateMatchCore.TemplatePathKey] = Path.GetFileName(path);
            }
        }

        return bot;
    }

    private static string Get(BotAction action, string key)
        => action.Config.TryGetValue(key, out var v) ? ConfigValues.AsString(v) : string.Empty;
}
