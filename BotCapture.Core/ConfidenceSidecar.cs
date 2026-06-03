using System.Text.Json;
using System.Text.Json.Serialization;

namespace BotCapture.Core;

/// <summary>Reads/writes the <c>&lt;image&gt;.png.meta.json</c> sidecar that stores a template's chosen
/// confidence threshold. Writes are best-effort; reads never throw — a missing or corrupt sidecar
/// yields the supplied fallback.</summary>
public static class ConfidenceSidecar
{
    private const string Suffix = ".meta.json";

    private sealed record Meta([property: JsonPropertyName("confidence")] double Confidence);

    /// <summary>The sidecar path for an image (e.g. <c>attack-btn.png</c> -> <c>attack-btn.png.meta.json</c>).</summary>
    public static string PathFor(string imagePath) => imagePath + Suffix;

    public static void Write(string imagePath, double confidence)
    {
        var json = JsonSerializer.Serialize(new Meta(confidence));
        File.WriteAllText(PathFor(imagePath), json);
    }

    /// <summary>Reads the sidecar confidence, or returns <paramref name="fallback"/> if the sidecar is
    /// absent or unreadable.</summary>
    public static double Read(string imagePath, double fallback)
    {
        try
        {
            var path = PathFor(imagePath);
            if (!File.Exists(path))
            {
                return fallback;
            }

            var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(path));
            return meta?.Confidence ?? fallback;
        }
        catch
        {
            return fallback; // missing/corrupt/locked sidecar -> caller's default
        }
    }
}
