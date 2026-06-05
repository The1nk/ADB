using System.Drawing;
using AdbCore.Actions.BuiltIn;
using AdbCore.Screen;

namespace AdbCore.Ocr;

/// <summary>Capture-source-independent OCR helpers shared by the Screen and Android OCR actions. Reuses
/// <see cref="TemplateMatchCore"/> for ROI resolution (and, in the actions, match-variable writing).</summary>
public static class OcrCore
{
    /// <summary>Recognizes the configured ROI of <paramref name="image"/> (or the whole image when no ROI),
    /// returning word boxes in full-image coordinates (offset back by the ROI origin).</summary>
    public static OcrResult RecognizeRegion(Bitmap image, IReadOnlyDictionary<string, object> config, IOcrEngine engine)
    {
        var region = TemplateMatchCore.ResolveRegion(config, image.Width, image.Height);
        if (region is not Rectangle roi)
        {
            return engine.Recognize(image);
        }

        using var crop = image.Clone(roi, image.PixelFormat);
        var result = engine.Recognize(crop);
        var offset = new List<OcrWord>(result.Words.Count);
        foreach (var w in result.Words)
        {
            var b = w.Bounds;
            offset.Add(w with { Bounds = new Rectangle(b.X + roi.X, b.Y + roi.Y, b.Width, b.Height) });
        }
        return result with { Words = offset };
    }

    /// <summary>Finds the first word whose text contains <paramref name="target"/> (case-insensitive, trimmed)
    /// with confidence ≥ <paramref name="minConfidence"/>; null when none. Returns the word box as a
    /// <see cref="MatchResult"/> (score = the word's confidence).</summary>
    public static MatchResult? FindWord(OcrResult result, string target, double minConfidence)
    {
        var needle = target.Trim().ToLowerInvariant();
        if (needle.Length == 0)
        {
            return null;
        }

        foreach (var w in result.Words)
        {
            if (w.Confidence >= minConfidence && w.Text.ToLowerInvariant().Contains(needle))
            {
                return new MatchResult(w.Bounds.X, w.Bounds.Y, w.Bounds.Width, w.Bounds.Height, w.Confidence);
            }
        }

        return null;
    }
}
