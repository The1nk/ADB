using System.Drawing;

namespace AdbCore.Screen;

/// <summary>Finds a template image within a haystack bitmap. Returns the single best match when its score
/// meets <paramref name="minConfidence"/> (0–1), else null. The template is supplied as in-memory PNG bytes
/// only (templates are always embedded in the .bot); there is no file-path overload. Throws if the template
/// bytes can't be decoded.</summary>
public interface ITemplateMatcher
{
    MatchResult? Match(Bitmap haystack, byte[] templatePng, double minConfidence);
}
