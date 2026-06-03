using System.Drawing;
using AdbCore.Screen;

namespace AdbCore.Tests.Screen;

internal sealed class FakeWindowCapture(int width, int height) : IWindowCapture
{
    public int Calls { get; private set; }
    public ScreenCaptureMethod LastMethod { get; private set; }

    public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method)
    {
        Calls++;
        LastMethod = method;
        return new Bitmap(width, height);
    }
}

internal sealed class FakeTemplateMatcher(MatchResult? result) : ITemplateMatcher
{
    public int LastHaystackWidth { get; private set; }
    public int LastHaystackHeight { get; private set; }
    public string? LastTemplatePath { get; private set; }
    public double LastConfidence { get; private set; }

    public MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence)
    {
        LastHaystackWidth = haystack.Width;
        LastHaystackHeight = haystack.Height;
        LastTemplatePath = templatePath;
        LastConfidence = minConfidence;
        return result;
    }
}

internal sealed class FixedRandomSource(int value) : IRandomSource
{
    public int Next(int minInclusive, int maxInclusive) => value;
}
