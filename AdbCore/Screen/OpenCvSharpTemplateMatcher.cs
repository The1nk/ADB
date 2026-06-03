using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace AdbCore.Screen;

/// <summary>Template matching via OpenCvSharp (<c>TM_CCOEFF_NORMED</c>, single best match).</summary>
public sealed class OpenCvSharpTemplateMatcher : ITemplateMatcher
{
    public MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence)
    {
        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Template image not found: '{templatePath}'.", templatePath);
        }

        using var template = Cv2.ImRead(templatePath, ImreadModes.Color);
        if (template.Empty())
        {
            throw new InvalidOperationException($"Template image could not be read: '{templatePath}'.");
        }

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
