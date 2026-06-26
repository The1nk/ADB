using System.Drawing;

namespace AdbCore.Screen;

/// <summary>Finds a template image within a haystack bitmap. Returns the single best match when its score
/// meets <paramref name="minConfidence"/> (0–1), else null. The template is supplied either by file path or
/// by in-memory image bytes (for templates embedded in a .bot). Throws if the template can't be read/decoded.</summary>
public interface ITemplateMatcher
{
    MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence);

    MatchResult? Match(Bitmap haystack, byte[] templatePng, double minConfidence);
}
