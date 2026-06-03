using System.Text.Json;

namespace BotBuilder.Core.Integration;

/// <summary>Reads the confidence threshold from an image's <c>&lt;image&gt;.png.meta.json</c> sidecar (the
/// format BotCapture writes). Returns null when the sidecar is absent, unreadable, or has no numeric
/// <c>confidence</c>. A read-only mirror of BotCapture's sidecar writer, kept here so the Builder needn't
/// reference BotCapture.</summary>
public static class ConfidenceSidecarReader
{
    public static double? Read(string imagePath)
    {
        try
        {
            var path = imagePath + ".meta.json";
            if (!File.Exists(path))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetDouble()
                : null;
        }
        catch
        {
            return null; // missing/corrupt/locked sidecar
        }
    }
}
