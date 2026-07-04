using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace AdbCore.Screen;

/// <summary>Template matching via OpenCvSharp (<c>TM_CCOEFF_NORMED</c>, single best match). The template is
/// supplied as in-memory image bytes (templates embedded in a .bot) — there is no file-path overload.</summary>
public sealed class OpenCvSharpTemplateMatcher : ITemplateMatcher
{
    public MatchResult? Match(Bitmap haystack, byte[] templatePng, double minConfidence)
    {
        if (templatePng is null || templatePng.Length == 0)
        {
            throw new InvalidOperationException("Embedded template image is empty.");
        }

        using var template = Cv2.ImDecode(templatePng, ImreadModes.Color);
        if (template.Empty())
        {
            throw new InvalidOperationException("Embedded template image could not be decoded.");
        }

        return MatchMat(haystack, template, minConfidence);
    }

    private static MatchResult? MatchMat(Bitmap haystack, Mat template, double minConfidence)
    {
        using var source = haystack.ToMat();          // BGRA from a 32bpp bitmap
        using var sourceBgr = new Mat();
        Cv2.CvtColor(source, sourceBgr, ColorConversionCodes.BGRA2BGR);

        if (template.Width > sourceBgr.Width || template.Height > sourceBgr.Height)
        {
            return null; // template larger than haystack (e.g. ROI smaller than template)
        }

        using var result = new Mat();
        Cv2.MatchTemplate(sourceBgr, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

        if (maxVal < minConfidence)
        {
            return null;
        }

        return new MatchResult(maxLoc.X, maxLoc.Y, template.Width, template.Height, maxVal);
    }
}
