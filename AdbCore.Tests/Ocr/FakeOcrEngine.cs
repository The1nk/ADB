using System.Drawing;
using AdbCore.Ocr;

namespace AdbCore.Tests.Ocr;

/// <summary>A canned OCR engine for tests: returns a fixed OcrResult and records the image size it saw.</summary>
internal sealed class FakeOcrEngine : IOcrEngine
{
    private readonly OcrResult _result;
    public int LastWidth { get; private set; }
    public int LastHeight { get; private set; }

    public FakeOcrEngine(OcrResult result) => _result = result;

    public OcrResult Recognize(Bitmap image)
    {
        LastWidth = image.Width;
        LastHeight = image.Height;
        return _result;
    }
}
