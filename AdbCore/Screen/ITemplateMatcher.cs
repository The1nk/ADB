using System.Drawing;

namespace AdbCore.Screen;

/// <summary>Finds a template image within a haystack bitmap. Returns the single best match when its score
/// meets <paramref name="minConfidence"/> (0–1), else null. Throws if the template can't be read.</summary>
public interface ITemplateMatcher
{
    MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence);
}
